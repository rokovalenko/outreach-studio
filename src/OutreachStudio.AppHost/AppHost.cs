var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("outreach-studio-postgres")
    .WithLifetime(ContainerLifetime.Persistent);
var db = postgres.AddDatabase("outreach");

var providers = builder.AddProject<Projects.OutreachStudio_Providers>("providers");

var web = builder.AddProject<Projects.OutreachStudio_Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(db).WaitFor(db)
    .WithReference(providers);

providers.WithReference(web);

builder.AddProject<Projects.OutreachStudio_Worker>("worker")
    .WithReference(db).WaitFor(db)
    .WithReference(providers)
    .WaitFor(web)
    .WithReplicas(2);

builder.Build().Run();
