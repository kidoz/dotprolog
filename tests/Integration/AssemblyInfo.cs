// Almost every class here shells out to dotnet/MSBuild against the same src project outputs;
// parallel test collections race on shared bin/obj files (deps.json write locks, task builds).
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
