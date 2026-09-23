# Create a typed Prolog library

Use a `.dplproj` library and a `.dpli` contract to expose Prolog predicates as ordinary .NET
methods. Install the .NET 10 SDK, then run these commands in your projects directory:

```console
dotnet new install DotProlog.Templates
dotnet new prolog-lib -n PricingRules
```

The template pins the DotProlog SDK and compiler package in its generated project. Keep their
versions aligned when updating the project.

## Define the rules and contract

Replace `PricingRules/rules.pl` with:

```prolog
:- module(pricing, [discount/3, in_catalogue/1]).

discount(Price, Percent, Result) :-
    Result is Price * (100 - Percent) / 100.

in_catalogue(widget).
in_catalogue(gadget).
```

Replace `PricingRules/rules.dpli` with:

```prolog
:- clr_module('Pricing').
:- clr_namespace('PricingRules').
:- clr_export(discount/3, det, [in(price, float), in(percent, integer), out(result, float)]).
:- clr_export(in_catalogue/1, semidet, [in(item, atom)]).
```

Keep the `.pl` and `.dpli` base names identical. Build the library:

```console
dotnet build PricingRules/PricingRules.dplproj
```

A successful build generates `IPricingModule` and `PricingModule` in the `PricingRules` namespace.
The [contract reference](../dotnet-integration.md#define-a-dplproj-library) describes how argument
modes and determinism determine method signatures.

## Call the generated methods

Create a consumer beside the library:

```console
dotnet new console -n PricingApp --framework net10.0
```

Add this item group inside `PricingApp/PricingApp.csproj`'s `Project` element:

```xml
<ItemGroup>
  <ProjectReference Include="../PricingRules/PricingRules.dplproj" />
</ItemGroup>
```

Replace `PricingApp/Program.cs` with:

```csharp
using PricingRules;

IPricingModule pricing = PricingModule.Create();
Console.WriteLine(pricing.Discount(100.0, 15));
Console.WriteLine(pricing.InCatalogue("widget"));
```

Run the consumer:

```console
dotnet run --project PricingApp/PricingApp.csproj
```

Expect `85` and `True` on separate lines. The `.dplproj` is built through the project reference.
For F# and Visual Basic consumers and repository examples, see
[reference Prolog from another .NET language](reference-project.md).
