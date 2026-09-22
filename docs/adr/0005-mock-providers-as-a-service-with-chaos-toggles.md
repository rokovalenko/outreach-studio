# ADR-005: Mock providers are a separate service with chaos toggles and simulated receipts

Status: accepted
Date: 2026-09-22

## Context

There is no real push or email provider in this repo and there never will be. The worker still needs something to call that behaves like one: latency, transient errors, outages, and delivery receipts that arrive late, out of order and sometimes twice. The demo needs to flip a provider down while a send is running and show failover in the delivery board and in traces.

## Decision

`OutreachStudio.Providers` is a small ASP.NET Core service with four providers behind one endpoint shape, `POST /{provider}/send`. `push-a` and `email-a` are the primaries, `push-b` and `email-b` are the failover targets. Each provider has a chaos record (added latency, error rate, hard down, duplicate receipts) held in memory and changed through `GET /chaos` and `PUT /chaos/{provider}`. The web app proxies those two endpoints so the delivery board can flip them.

After accepting a message the service simulates the provider's pipeline: a delivered receipt a few seconds later, an opened receipt for a share of them later still, posted to the web app's `POST /api/receipts`. With duplicate receipts on, delivered is posted twice. The receipt endpoint only moves a delivery forward (Sent to Delivered to Opened) and ignores anything else, so duplicates and out of order receipts are handled without a dedupe table.

The worker's send policy is fixed: three tries on the primary with short backoff, one on the secondary, then the row goes back to the outbox for a later pass, and after three passes it is dead-lettered. Every provider call is a `DeliveryAttempt` row, which is what the provider health tiles aggregate.

## Consequences

- Failover is visible in three places for the same delivery: the attempt rows, the child spans under the delivery span, and the provider column on the live board.
- Because the providers are a separate process, the trace of one delivery crosses two services and the Aspire dashboard shows it that way.
- The chaos settings are not persisted. Restarting the AppHost resets every provider to healthy, which is what a demo wants.
- Receipts depend on the web app being reachable from the providers service. Under Aspire that is service discovery. Outside Aspire the base address needs configuring.
- The provider interface in the worker has one method. Replacing a mock with a real adapter is one class and one registration.

## Alternatives considered

- In-process fake transports inside the worker. Simpler, but then there is no HTTP hop, no cross-service trace and the chaos toggles have to reach into the worker's memory from the web app.
- One provider per process. Four processes for four `if` blocks.
- Persisting chaos settings in Postgres. Survives restarts, which nobody wants for fault injection.
