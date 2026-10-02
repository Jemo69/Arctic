# harbor

The gateway. YARP reverse proxy, correlation IDs, rate limiting — and the one
place that strips any `X-Person-Id` (or similar) a caller sent itself, so
nobody can hand themselves someone else's identity on the way in.

It does not validate sessions. The session cookie is forwarded as-is; atlas
is what looks it up and decides whether the request may do what it is
asking. Harbor says who a request claims to be; the service decides whether
that is allowed.

Target size ~200 lines, mostly config. **No business logic ever goes here.** If you're reasoning about hackathon rules while editing harbor, you're in the wrong directory.

See `doc-starter/morganhacks-gateway.md` (local only — see `docs/README.md`).
