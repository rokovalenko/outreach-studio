# ADR-002: The rule tree is a typed AST interpreted in place

Status: accepted
Date: 2026-09-22

## Context

A campaign audience is a rule over profile attributes (country, tier, signup date, last active, platform, marketing consent, time zone, points balance) and over event counts in a time window ("reward claimed at least 2 times in the last 30 days"). The rule is edited in a tree builder, stored with the campaign version, evaluated in the browser on every edit and evaluated on the server at schedule time. The editor also needs to say, for any user, which clauses matched and with what value.

## Decision

The rule is a tree of C# records: `AndRule`, `OrRule`, `NotRule`, `AttributeCompare(attribute, op, value)` and `EventCountInWindow(event, op, count, windowDays)`. System.Text.Json polymorphism with a `kind` discriminator serialises it, so the wire format and the in-memory model are one definition and a campaign version stores exactly what the browser evaluated.

Attribute values are strings in the tree. `UserSchema` is the one list of attributes with their type, enum values and the operators each type allows. `RuleValidator` checks a tree against it (unknown attribute, operator not allowed for the type, value that does not parse) before anything evaluates it, and the builder renders its inputs from the same list.

`RuleEvaluator` interprets the tree. `Matches` is the fast path used for counting, `Explain` returns a `ClauseResult` tree with each clause's actual value, which is the match explanation panel. Event clauses read from `AudienceUser.Events`, a dictionary of event type to sorted unix timestamps, so a count in a window is two binary searches. Those arrays are built once when the snapshot is loaded, not per evaluation.

The audience snapshot the browser downloads is the whole user base, not a sample. It goes over the wire in a binary format from the engine (`AudienceSnapshotFormat`, a `BinaryWriter` stream with delta coded event timestamps) rather than JSON. Measured on the seeded base of 20,000 users and about 400,000 events: JSON was 12.5 MB, 4.9 MB with Brotli, and took the WebAssembly interpreter about 20 seconds to parse. The binary stream is 3.4 MB, 2.5 MB with Brotli, and loads in about 1.5 seconds. One evaluation over all users takes about 250 ms in the browser and 15 ms on the server. The evaluation chip in the editor shows both numbers live.

## Consequences

- Adding an attribute is one line in `UserSchema` plus a column. Adding an operator is a case in the evaluator and a label in the builder.
- Evaluation allocates nothing on the fast path except the enumerator, so twenty thousand users evaluate in a quarter of a second in the interpreted browser runtime and in a few milliseconds on the server.
- Values are strings, so a rule can be syntactically valid JSON and semantically wrong. The validator is the guard and it runs before save, before estimate and before schedule.
- The tree has no variables, functions or cross-user aggregates. A rule like "users whose points are above the median" cannot be written. That is a deliberate limit for v1.

## Alternatives considered

- Expression trees compiled with `Expression.Compile`. In browser WebAssembly there is no JIT, so the compiled delegate runs through the interpreter anyway, and an expression tree gives no per-clause explanation without a second walk.
- A string DSL with a parser. More to build, more to explain in the UI, and the builder would generate strings that it then has to parse back.
- Translating the tree to SQL for the server side. Tempting for the send, but then the browser and the server evaluate different code and the same-assembly claim in ADR-001 is gone. At twenty thousand users the in-memory evaluation is fast enough.
- Sampling the user base in the browser. Smaller download, but the count becomes an estimate with error bars and the sample of matched users may be empty for narrow rules.
- JSON for the snapshot. It was the first version. The download was fine, the parse in the interpreter was not, and ahead of time compilation of the client would have cost more build time than a fifty line binary format.
