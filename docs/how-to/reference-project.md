# Reference Prolog from another .NET language

Start with an existing `.dplproj` library whose `.dpli` contract exports the methods you need.
If you do not have one, [create a typed library](create-library.md) first.

Add a project reference to your C#, F#, or Visual Basic project, adjusting the relative path:

```xml
<ItemGroup>
  <ProjectReference Include="../PricingRules/PricingRules.dplproj" />
</ItemGroup>
```

Import the namespace declared by the contract's `clr_namespace/1` directive, or the library's
root namespace when that directive is absent. Call the generated interface and factory as you
would any other .NET library. The [library guide](create-library.md#call-the-generated-methods)
contains a complete C# consumer.

## Run the repository consumers

From the DotProlog repository root with the .NET 10 SDK installed:

```console
dotnet build src/DotProlog.Build.Tasks
dotnet run --project samples/PricingConsole
dotnet run --project samples/PricingFSharp
dotnet run --project samples/PricingVisualBasic
```

Each consumer calls the same `samples/PricingRules` library. Check their output for the discounted
price `85` and the catalogue results. Their source shows each language's syntax for the generated
methods and streamed results.

If adding a `.dplproj` to a `.slnx` solution by hand, give its `Project` entry `Type="C#"`; the SDK
uses the C# build pipeline. See [SDK reference](../dotnet-integration.md#reference-prolog-from-another-net-language)
for language settings and [runtime architecture](../architecture/index.md#sdk-and-generated-facades)
for how Prolog becomes an ordinary assembly.
