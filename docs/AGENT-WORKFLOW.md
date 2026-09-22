# How this repo was built with agents

The repo was built in one working session by the author directing Claude Code, with three parallel coding agents for the large parts and a browser-driven QA pass at the end. This page records what was decided by a person, what was delegated, and where the agents were wrong.

## Split

1. The author fixed the scope in a brief outside the repo: single-shot campaigns, loyalty vocabulary, Blazor Web App with interactive auto, MudBlazor, Aspire, Postgres, OpenTelemetry, no hosted demo, no AI feature.
2. The lead agent wrote the foundation by hand before delegating anything: solution layout, the engine assembly (rule tree, evaluator, personalisation, scheduling), the data model and migration, the synthetic generator and seeder, the mock provider service, the API contract between the browser editor and the server, and the six ADRs. Those are the contracts everything else was built against.
3. Three agents then worked in parallel in separate git worktrees, each with a written brief that named the files it owned and the files it must not touch:
   - the worker and its Testcontainers suite,
   - the server half of the web app (pages, endpoints, `CampaignService`, the notification listener),
   - the WebAssembly campaign editor.
4. The lead agent merged the three branches, fixed what the merge exposed, ran the stack under Aspire and drove it in a browser with Playwright.

## What the agents got wrong and what caught it

- Two agents both rendered the MudBlazor popover and snackbar providers, one in the layout as a server island and one inside the WebAssembly editor page. The first visit to the editor threw a duplicate section error. Caught in the browser, fixed by moving the providers into every page's own island.
- The web agent registered the DbContext by hand because Aspire's pooled registration rejected a naming convention set in `OnConfiguring`. The worker agent used the pooled registration and would have failed at runtime for the same reason, its tests passed because they registered the context their own way. Fixed by one static `Configure` method that every registration calls, including the test fixture.
- The seeder marked holdout rows only for finished campaigns, so the campaign that starts sending on first boot showed a holdout of zero. Caught on the live board.
- The live board counted provider successes from delivery notifications, which carry only the provider that finally succeeded, so a primary outage showed zero failures on the primary. Fixed by reading the attempt rows instead.
- A rule stored in a `jsonb` column comes back with its keys reordered, and the polymorphic deserialiser refused a tree whose `kind` was not first. The web agent found it and fixed the JSON options.
- The audience snapshot was JSON. Nobody measured it until the browser QA pass, where it took twenty seconds to parse in WebAssembly. The lead agent replaced it with a binary format in the engine and recorded the numbers in ADR-002.
- Three breakdown charts sat side by side in a panel too narrow for them, so every bar was five pixels wide. Caught in a screenshot, not in any test.
- The seeded drafts had a start time later in the day, so scheduling one in the demo queued everything for noon and the live board stayed still. Caught while recording the gif.

## What stayed with the person

The scope, the vocabulary, the stack, the rule engine design, every ADR, the wording of the docs, the decision to keep the mock providers as a separate process, and the review of each agent's report before its branch was merged. The agents wrote most of the lines. The shape of the system was decided before they started.
