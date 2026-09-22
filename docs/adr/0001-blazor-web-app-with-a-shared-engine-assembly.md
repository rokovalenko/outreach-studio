# ADR-001: Blazor Web App on .NET 10 with one engine assembly for the browser and the server

Status: accepted
Date: 2026-09-22

## Context

The app is a back office for campaign managers: a dashboard, a campaign editor, an approval flow, a live delivery board and results. Most of it is forms and tables, which Blazor Server handles well. One part is different. The audience estimate has to update as the rule tree is edited, over a user base of twenty thousand users, and a round trip per keystroke would make it feel like a search box rather than a calculator.

We also want the repo to make one claim that only Blazor can make honestly: the code that previews an audience in the browser is the code that selects it on the server.

## Decision

The web app is a Blazor Web App with the interactive auto render mode. Pages that read and write the database (dashboard, campaign page, live board, results, providers, suppressions) are interactive server components. The campaign editor is an interactive auto component that lives in the client project, so it renders over the server circuit on first visit and in WebAssembly once the runtime has downloaded.

`OutreachStudio.Engine` is a class library with no package references. It holds the rule tree, the evaluator, the audience estimate, personalisation, quiet hours, the holdout hash and the rollout planner. The browser project, the web server and the worker all reference it. The editor calls `AudienceEstimate.Compute` in the browser, `CampaignService.ScheduleAsync` calls `AudienceEstimate.Select` on the server, and the worker calls `Personalisation.Render` and `QuietHours.NextAllowed` at send time. There is one test project for it and it runs on the server runtime, the browser path is the same IL.

The editor talks to two interfaces, `ICampaignEditorApi` and `IAudienceSource`. The server registers implementations over the database, the browser registers implementations over JSON endpoints. The component does not know which one it has.

Processes are composed by an Aspire AppHost: Postgres in a container, the web app, two worker replicas and the mock provider service. `dotnet run` on the AppHost starts everything and the Aspire dashboard shows traces, metrics and logs from all of them.

## Consequences

- The browser downloads the .NET runtime, MudBlazor and the user base. The first visit to the editor is slower than any other page. The audience download is measured in ADR-002.
- Two implementations of each editor interface exist. They are thin (one HTTP call per method on the browser side) and the cost is the price of the auto mode.
- Enum values cross the wire as names and the rule tree crosses as its JSON form, so the JSON options in the browser and on the server must agree. Both use `Rule.JsonOptions`.
- The Aspire dashboard is the observability UI. Nothing in the app duplicates it.
- Without Docker there is no Postgres and the AppHost does not start. The README says so.

## Alternatives considered

- Blazor Server only. Simplest and it fits a back office, but the estimate becomes a request per edit and the same-assembly claim disappears.
- A Vue or React front end with a .NET API. That is the other portfolio repo's shape. It would need the rule engine ported to TypeScript or called over HTTP, which is exactly what we are avoiding.
- Standalone WebAssembly with an API. Loses prerendering and the server-side pages would have to become an API plus client code for no gain.
- Docker compose with the standalone OpenTelemetry collector and dashboard instead of Aspire. Kept as the fallback if Aspire had not supported .NET 10. It does since Aspire 13, so the fallback was not needed.
