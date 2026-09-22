var builder = DistributedApplication.CreateBuilder(args);

// A fixed password, because the data volume persists between runs and a generated one would not match it.
var password = builder.AddParameter("postgres-password", "outreach-local", secret: true);
var postgres = builder.AddPostgres("postgres", password: password)
    .WithDataVolume("outreach-studio-postgres")
    .WithLifetime(ContainerLifetime.Persistent);
var db = postgres.AddDatabase("outreach");

var providers = builder.AddProject<Projects.OutreachStudio_Providers>("providers");

var web = builder.AddProject<Projects.OutreachStudio_Web>("web")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(db).WaitFor(db)
    .WithReference(providers);

providers.WithReference(web);

builder.AddProject<Projects.OutreachStudio_Worker>("worker")
    .WithReference(db).WaitFor(db)
    .WithReference(providers)
    .WaitFor(web)
    .WithReplicas(2);

builder.Build().Run();
