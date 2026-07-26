module PricingFSharp.Program

open PricingRules

/// Calls the same generated facade the C# sample uses, to show that a .dplproj is an ordinary
/// assembly and needs nothing language-specific to consume.
[<EntryPoint>]
let main _ =
    let pricing = PricingModule.Create()

    printfn "F#: 100 less 15%% = %g" (pricing.Discount(100.0, 15L))
    printfn "F#: tier of 1200 is %s" (pricing.Tier 1200L)
    printfn "F#: widget in catalogue: %b" (pricing.InCatalogue "widget")

    // The nondet export is an IEnumerable, so F# sequence expressions work on it directly.
    let bundles =
        pricing.Bundle [| "widget"; "gadget" |]
        |> Seq.map (fun bundle -> String.concat "+" bundle)
        |> String.concat ", "

    printfn "F#: bundles: %s" bundles

    // The semidet export with an output is a Try method, which F# turns into a tuple return.
    match pricing.TryStockLevel "widget" with
    | true, level -> printfn "F#: widget stock is %d" level
    | false, _ -> printfn "F#: widget stock unknown"

    0
