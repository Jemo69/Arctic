# runbooks

What to do when something breaks. **Written before the event, not during** —
nobody writes documentation at 2am on event Saturday.

One page each, each ending with an escalation name.

Two environments exist now: staging (`rg-mh-staging`, centralus) and
production (`rg-mh-prod`, eastus2) — different resource groups, different
region, and every `ca-*`/`psql-*` name carries the environment in it. A
command copied from one without changing the suffix runs against the wrong
place rather than failing, so check which environment you are typing before
you run anything here.

## Written

- [Rolling back a bad deploy](rolling-back-a-deploy.md) — measured at ~7 minutes
- [Getting at the database](database-access.md) — and why there is no jump box
- [Nobody is receiving magic links](nobody-is-receiving-magic-links.md) — the
  failure where everything looks healthy
- [The first production deploy](first-production-deploy.md) — the checklist
  that took production live, kept for the next environment that needs one
- [Mail is not arriving](mail-is-not-arriving.md) — three causes that all look
  like success, and the one test that tells them apart

## Noise you can ignore

Every container app in both environments logs `ScaledObjectCheckFailed: no
triggers defined in the ScaledObject` roughly every ten minutes, since
2026-09-12. Container Apps creates a KEDA `ScaledObject` per app, and an app
with no scale rule has one with no triggers — which is most of them, since
only lark gets a scale rule and only when `LARK_WARM_REPLICAS=0`. It is not
evidence of anything breaking. Worth saying here once so nobody spends an
incident chasing it.

## Still to write

- [ ] Registration form returning errors — the form exists now, so this one is
      only waiting on somebody writing it
- [ ] Broadcast circuit breaker tripped — waits on the breaker existing (M7)
- [ ] Check-in scanners offline at the venue — phase two
- [ ] Database connection exhaustion
- [ ] MLH code-of-conduct incident response — a procedure, not a feature

## What makes one of these worth having

Written from something somebody actually did, with real numbers. The rollback
page says seven minutes because a rollback was performed and timed, not because
seven felt about right. A runbook whose timings are guesses is one nobody trusts
the second time.

Every page ends with who to escalate to, because the worst moment to work out
whose problem something is, is while it is happening.
