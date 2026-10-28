// Load test: one host drives a game of QUESTIONS questions while PLAYERS players join, answer and receive results,
// all through the Web origin (YARP) and the SignalR hub over WebSockets (docs/load-test.md).
//
//   k6 run -e PLAYERS=2000 tests/load/quiz.js
import http from 'k6/http';
import ws from 'k6/ws';
import { check, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://localhost:5255';
const WS_BASE = BASE.replace(/^http/, 'ws');
const PLAYERS = parseInt(__ENV.PLAYERS || '2000');
const QUESTIONS = parseInt(__ENV.QUESTIONS || '10');
const JOIN_WINDOW_S = parseInt(__ENV.JOIN_WINDOW || '60');   // players arrive spread over this time
const QUESTION_PERIOD_S = parseInt(__ENV.PERIOD || '10');   // the host moves on this often
const API_STATS = (__ENV.API_STATS || 'http://localhost:5197,http://localhost:5198').split(',');
const RS = '\x1e';

const joinDuration = new Trend('quiz_join_duration', true);          // POST /api/sessions/join
const hubJoinDuration = new Trend('quiz_hub_join_duration', true);   // WebSocket open + JoinAsPlayer completion
const questionLatency = new Trend('quiz_question_latency', true);    // host's scheduled send -> QuestionStarted received
const answerRoundTrip = new Trend('quiz_answer_roundtrip', true);    // SubmitAnswer sent -> completion received
const resultLatency = new Trend('quiz_result_latency', true);        // answer sent -> own PlayerResult received (the question closes when the last player has answered)
const joinFailures = new Counter('quiz_join_failures');
const hubFailures = new Counter('quiz_hub_failures');
const questionsMissed = new Counter('quiz_questions_missed');         // QuestionStarted a player never got
const resultsMissed = new Counter('quiz_results_missed');             // PlayerResult a player never got
const finalsMissed = new Counter('quiz_finals_missed');
const answersRejected = new Counter('quiz_answers_rejected');         // SubmitAnswer that was not Accepted
const questionsReceived = new Counter('quiz_questions_received');

export const options = {
  scenarios: {
    host: { executor: 'per-vu-iterations', vus: 1, iterations: 1, exec: 'host', maxDuration: '15m' },
    players: { executor: 'per-vu-iterations', vus: PLAYERS, iterations: 1, exec: 'player', maxDuration: '15m' },
  },
  thresholds: {
    quiz_join_failures: ['count==0'],
    quiz_hub_failures: ['count==0'],
    quiz_questions_missed: ['count==0'],
    quiz_results_missed: ['count==0'],
    quiz_finals_missed: ['count==0'],
    quiz_question_latency: ['p(95)<2000'],
    quiz_answer_roundtrip: ['p(95)<1000'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

function json(res) { return JSON.parse(res.body); }

export function setup() {
  const email = `load-${Date.now()}@example.test`;
  const password = 'Load-Test-Password-1';
  const jar = http.cookieJar();
  check(http.post(`${BASE}/api/auth/register`, JSON.stringify({ email, password }), { headers: { 'Content-Type': 'application/json' } }),
    { registered: r => r.status === 200 });
  const login = http.post(`${BASE}/api/auth/login?useCookies=true`, JSON.stringify({ email, password }), { headers: { 'Content-Type': 'application/json' } });
  check(login, { 'logged in': r => r.status === 200 });
  const cookie = Object.entries(jar.cookiesForURL(BASE)).map(([k, v]) => `${k}=${v}`).join('; ');

  const questions = [];
  for (let q = 0; q < QUESTIONS; q++) {
    questions.push({
      text: `Question ${q + 1}?`, timeLimitSeconds: 20,
      options: [0, 1, 2, 3].map(i => ({ text: `Option ${i}`, isCorrect: i === q % 4 })),
    });
  }
  const headers = { 'Content-Type': 'application/json', Cookie: cookie };
  const quiz = json(http.post(`${BASE}/api/quizzes`, JSON.stringify({ title: 'Load test', description: 'k6', questions }), { headers }));
  const session = json(http.post(`${BASE}/api/sessions`, JSON.stringify({ quizId: quiz.id }), { headers }));

  // The game starts at a fixed moment, so every VU can measure latency against the same schedule.
  const startAt = Date.now() + (JOIN_WINDOW_S + 25) * 1000;
  return { cookie, sessionId: session.sessionId, pin: session.pin, startAt };
}

// --- minimal SignalR JSON protocol client -------------------------------------------------------------------------

function hubSession(socket) {
  let nextId = 1;
  const pending = {};
  let buffer = '';
  const hub = {
    onInvocation: () => {},
    invoke(target, args, onDone) {
      const id = String(nextId++);
      if (onDone) pending[id] = onDone;
      socket.send(JSON.stringify({ type: 1, invocationId: id, target, arguments: args }) + RS);
    },
    feed(data, onHandshake) {
      buffer += data;
      let i;
      while ((i = buffer.indexOf(RS)) >= 0) {
        const frame = buffer.slice(0, i);
        buffer = buffer.slice(i + 1);
        if (frame === '') continue;
        const msg = JSON.parse(frame);
        if (msg.type === undefined) onHandshake();
        else if (msg.type === 1) hub.onInvocation(msg.target, msg.arguments);
        else if (msg.type === 3) { const cb = pending[msg.invocationId]; delete pending[msg.invocationId]; if (cb) cb(msg); }
        // type 6 is the server's ping; nothing to answer
      }
    },
  };
  return hub;
}

// Opens a hub connection and calls init(hub, socket) once the SignalR handshake is done.
function connect(path, headers, maxMs, init) {
  return ws.connect(`${WS_BASE}${path}`, { headers }, socket => {
    const hub = hubSession(socket);
    socket.on('open', () => {
      socket.send(JSON.stringify({ protocol: 'json', version: 1 }) + RS);
      socket.setInterval(() => socket.send('{"type":6}' + RS), 10000); // keep-alive: the server drops silent clients
    });
    let ready = false;
    socket.on('message', data => hub.feed(data, () => { if (!ready) { ready = true; init(hub, socket); } }));
    socket.on('error', () => hubFailures.add(1));
    socket.setTimeout(() => socket.close(), maxMs);
  });
}

// --- host -----------------------------------------------------------------------------------------------------------

export function host(data) {
  const budget = (data.startAt - Date.now()) + (QUESTIONS + 3) * QUESTION_PERIOD_S * 1000 + 30000;
  connect('/hubs/quiz', { Cookie: data.cookie }, budget, (hub, socket) => {
    hub.invoke('JoinAsHost', [data.sessionId], joined => {
      if (joined.error) { hubFailures.add(1); socket.close(); return; }
      let asked = 0;
      const step = () => {
        hub.invoke(asked === 0 ? 'StartGame' : 'NextQuestion', [data.sessionId], msg => {
          if (msg.error) { socket.setTimeout(step, 1000); return; } // the results are not on screen yet: try again shortly
          asked++;
          if (asked <= QUESTIONS) socket.setTimeout(step, Math.max(1, data.startAt + asked * QUESTION_PERIOD_S * 1000 - Date.now()));
          else socket.setTimeout(() => socket.close(), 5000);       // the call after the last question shows the final results
        });
      };
      socket.setTimeout(step, Math.max(1, data.startAt - Date.now()));
    });
  });
}

// --- player ---------------------------------------------------------------------------------------------------------

export function player(data) {
  sleep(Math.random() * JOIN_WINDOW_S);

  const t0 = Date.now();
  const res = http.post(`${BASE}/api/sessions/join`, JSON.stringify({ pin: data.pin, nickname: `P${__VU}` }),
    { headers: { 'Content-Type': 'application/json' } });
  joinDuration.add(Date.now() - t0);
  if (res.status !== 200) { joinFailures.add(1); return; }
  const me = json(res);

  const gotQuestion = new Set();
  const gotResult = new Set();
  const answeredAt = {};
  let gotFinal = false;
  const budget = (data.startAt - Date.now()) + (QUESTIONS + 3) * QUESTION_PERIOD_S * 1000 + 30000;
  const hubStart = Date.now();

  connect('/hubs/quiz', {}, budget, (hub, socket) => {
    hub.onInvocation = (target, args) => {
      const received = Date.now();
      if (target === 'QuestionStarted') {
        const q = args[0];
        if (gotQuestion.has(q.index)) return;
        gotQuestion.add(q.index);
        questionsReceived.add(1);
        questionLatency.add(received - (data.startAt + q.index * QUESTION_PERIOD_S * 1000));
        // a player thinks for a moment, then taps
        socket.setTimeout(() => {
          const sent = Date.now();
          answeredAt[q.index] = sent;
          hub.invoke('SubmitAnswer', [q.index, Math.floor(Math.random() * 4)], msg => {
            answerRoundTrip.add(Date.now() - sent);
            if (msg.error || msg.result !== 0) answersRejected.add(1); // AnswerStatus.Accepted is 0
          });
        }, 500 + Math.random() * 3000);
      } else if (target === 'PlayerResult') {
        const r = args[0];
        gotResult.add(r.questionIndex);
        if (answeredAt[r.questionIndex]) resultLatency.add(received - answeredAt[r.questionIndex]);
      } else if (target === 'SessionFinished') {
        gotFinal = true;
        socket.setTimeout(() => socket.close(), 500);
      }
    };
    hub.invoke('JoinAsPlayer', [data.sessionId, me.playerId, me.reconnectToken], msg => {
      if (msg.error) { hubFailures.add(1); socket.close(); return; }
      hubJoinDuration.add(Date.now() - hubStart);
    });
  });

  // Checked after the socket closed: a player that missed an event never saw it.
  for (let q = 0; q < QUESTIONS; q++) {
    if (!gotQuestion.has(q)) questionsMissed.add(1);
    if (!gotResult.has(q)) resultsMissed.add(1);
  }
  if (!gotFinal) finalsMissed.add(1);
}

// --- server side numbers --------------------------------------------------------------------------------------------

export function teardown() {
  for (const api of API_STATS) {
    const res = http.get(`${api}/api/diagnostics/grain-writes`);
    console.log(`grain-writes ${api}: ${res.body}`);
  }
}
