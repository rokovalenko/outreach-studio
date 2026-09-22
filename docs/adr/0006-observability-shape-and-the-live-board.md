# ADR-006: One span per delivery, funnel counts as metrics, and a live board fed by Postgres notifications

Status: accepted
Date: 2026-09-22

## Context

When a provider fails mid-send the campaign manager asks what was delivered, what was retried and what is stuck. The engineer on call asks which provider, how slow and since when. Both questions need the same data at different granularity.

## Decision

Every service calls `AddServiceDefaults`, which configures OpenTelemetry tracing, metrics and logs with the OTLP exporter the Aspire AppHost injects. All custom instrumentation uses one `ActivitySource` and one `Meter`, both named `OutreachStudio`.

The worker opens one span per processed delivery, `deliver push` or `deliver email`, with the campaign, delivery, user, channel, provider, attempt number and outcome as attributes. Each provider call is a child span `send {provider}` with the try number, and the HTTP client instrumentation adds the request underneath. A retry chain is therefore a delivery span with several `send` children, and failover is the last child having a different provider name.

Campaign funnel counts are metrics beside the technical ones: `outreach.deliveries` counts every outcome with `status`, `channel`, `provider` and `reason` tags, `outreach.provider.latency` is a histogram per provider, `outreach.outbox.claimed` counts claims. The Aspire dashboard shows them without any configuration.

The live delivery board does not poll. A trigger on the `deliveries` table calls `pg_notify('delivery_events', json)` on every status change except Claimed. The web app holds one connection that listens on that channel, parses each payload into a `DeliveryEvent` and raises it to the board component, which updates its tiles and event stream. Updates reach the browser over the Blazor Server circuit, which is a SignalR connection, so there is no second hub.

Logs are structured and include the delivery and campaign ids as scope, so the Aspire dashboard links a log line to its trace.

## Consequences

- One delivery is one trace to open, whatever went wrong with it.
- Funnel numbers exist twice, as rows in the database and as counters. The rows are the truth, the counters are the timeline.
- `pg_notify` is best effort. If the listener reconnects during a send it misses the events in between, so the board seeds its counts from the database on open and the results page never relies on notifications.
- The Blazor circuit is the only realtime channel. There is no way to consume the board from outside the app, and there does not need to be.
- The Aspire dashboard is a development tool. Nothing here ships to a production backend, and the ADR does not claim to.

## Alternatives considered

- A dedicated SignalR hub for delivery events. Adds a hub, a client and a JS interop path for something the circuit already does.
- Polling the counts every second. Works and is simpler to reason about, but the event stream on the board would show batches rather than a flow.
- Emitting delivery events from the worker over HTTP to the web app. Couples the worker to the web app's address and drops events when the web app restarts. The trigger has neither problem.
