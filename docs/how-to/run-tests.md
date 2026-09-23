# Run Prolog tests

Use a `.dplproj` test project for predicates that should be checked by `dotnet test`.

## Run the repository example

From a checkout with the .NET 10 SDK installed, build the task assembly and run the sample:

```console
dotnet build src/DotProlog.Build.Tasks
dotnet test --project samples/PricingTests/PricingTests.dplproj
```

The runner discovers zero-arity predicates whose names start with `test_`. Each test passes if
its goal succeeds and runs in a fresh engine. Check the summary for zero failures.

## Create your own test project

With the .NET 10 SDK installed, run these commands in your projects directory:

```console
dotnet new install DotProlog.Templates
dotnet new prolog-test -n PrologChecks
```

Add a `.pl` file inside `PrologChecks` containing:

```prolog
test_addition :-
    2 + 2 =:= 4.

test_lists :-
    append([a, b], [c], [a, b, c]).
```

Run the project:

```console
dotnet test --project PrologChecks/PrologChecks.dplproj
```

Both new tests should pass, along with the template's tests. To check failure reporting, change
the expected arithmetic result to `5`, rerun, then restore `4`.

The template includes the Microsoft.Testing.Platform selection. When adding the project to a
solution, keep this setting in the solution-root `global.json`, alongside any SDK selection:

```json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```
