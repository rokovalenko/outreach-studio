# ADR-003: The deliveries table is the outbox and the unique key is the exactly-once guarantee

Status: accepted
Date: 2026-09-22

## Context

A campaign with an audience of several thousand users is sent by more than one worker process. Providers fail, workers restart, and the campaign manager needs to know that no user got the same message twice and that nothing was silently dropped. The state change "this campaign is now scheduled" and the work "send to these users" must not be able to disagree.

## Decision

The `deliveries` table is the outbox. `CampaignService.ScheduleAsync` runs one database transaction that evaluates the audience, plans the due time per user with `RolloutPlanner`, inserts one `Delivery` row per user per channel and moves the campaign to Scheduled. Either all of it commits or none of it does.

A unique index on `(campaign_id, user_id, channel)` makes a second row for the same user impossible, whatever code path tries to write it.

Workers claim rows with `UPDATE ... WHERE id IN (SELECT id FROM deliveries WHERE status = 'Queued' AND due_at <= now() ORDER BY due_at LIMIT n FOR UPDATE SKIP LOCKED) RETURNING *`. Two workers cannot claim the same row, and a worker that dies mid-batch leaves rows in Claimed that a periodic sweep returns to Queued after the claim timeout. A delivery moves to Sent once, in the same update that records the provider's message id.

A test in `OutreachStudio.Worker.Tests` runs six workers against one Postgres container (Testcontainers) with a fake provider that fails ten percent of calls at random, and asserts that the number of rows in Sent, the number of successful provider calls and the audience size are the same number. It runs in CI.

## Consequences

- Exactly-once holds at the boundary we control: one successful provider call per delivery. A provider that accepts a message and then delivers it twice is outside this boundary and shows up as a duplicate receipt, which the receipt endpoint ignores.
- The outbox row is the unit of retry, so the retry state (attempts, last error, next due time) lives on the row and is visible in the results page without a separate table.
- Postgres row locks are the only coordination between workers. There is no queue, no broker and no leader.
- Inserting a few thousand rows in one transaction takes a moment. At the scale of this demo that is under a second. A real system with millions of users would batch the inserts and mark the campaign Scheduled with a separate "planning" status.

## Alternatives considered

- A message broker with a consumer per channel. Adds a process, a client library and the same duplicate problem on the consumer side, which then needs an idempotency table that looks exactly like the deliveries table.
- Idempotency keys without the unique index, checked in code before send. Works until two workers check at the same moment.
- Marking the campaign Scheduled first and creating deliveries in a background job. Two states that can disagree after a crash, which is the problem the outbox exists to remove.
