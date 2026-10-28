// Browser-side helpers for the host panel. Cookies must be set by the browser itself, so sign-in/out
// are fetch calls to the same-origin /api proxy rather than server-to-server calls.
export async function postJson(url, body) {
    const response = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: body === null ? null : JSON.stringify(body),
        credentials: 'same-origin'
    });
    let payload = null;
    try { payload = await response.json(); } catch { /* empty or non-JSON body */ }
    return { ok: response.ok, status: response.status, body: payload };
}
