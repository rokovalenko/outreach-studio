# ADR-004: Guardrails run at send time in the worker, not at authoring time in the UI

Status: accepted
Date: 2026-09-22

## Context

Consent, suppression, frequency caps and quiet hours are the rules that keep a campaign from becoming a complaint. They can be checked when the campaign is built, but a user can revoke consent between approval and send, get suppressed by support an hour before the blast, or receive three other campaigns in the meantime. A check at authoring time is a check against stale data.

## Decision

`DeliveryPipeline` in the worker runs the guardrails on each delivery at the moment it is processed, in this order: marketing consent, suppression list, frequency cap across campaigns, quiet hours in the user's time zone. The first three record a skip with a reason on the row and stop. Quiet hours do not skip, they move `due_at` to the next allowed local time and put the row back in Queued, so the message goes out the next morning in the user's zone.

The editor still shows an estimate that includes users without consent if the rule does not filter them, so the campaign manager can see the difference between "reached" and "sent". The results page groups skips by reason.

The frequency cap and window are worker configuration (`Outbox:FrequencyCap`, `Outbox:FrequencyWindowDays`). The quiet hours are part of the campaign version because they are a decision the campaign manager makes per campaign.

## Consequences

- A user who revoked consent at 09:59 is skipped at 10:00. The skip is a row with a reason, not a log line.
- The audience size and the sent count differ for every campaign, and the gap has an explanation on the results page.
- The frequency cap is a query per delivery. With an index on `(user_id, sent_at)` it is cheap, and it stays correct when two campaigns send at the same time because both read the same table.
- Quiet hours can stretch a campaign by up to a day for users in far time zones. The rollout curve in the editor shows this before scheduling.
- There is no admin override to send during quiet hours. Setting both quiet hour fields to the same hour disables them for that campaign.

## Alternatives considered

- Filter at schedule time when the outbox is written. Cheaper, and consent is then checked once, but the window between schedule and send is exactly when the state changes.
- Filter in the rule tree by adding consent as a mandatory clause. Makes every rule longer and does nothing about suppression, caps or quiet hours.
- A separate guardrail service called before each send. One more process for four `if` statements.
