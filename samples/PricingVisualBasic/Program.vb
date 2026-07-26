' Visual Basic consuming the same generated facade as the C# and F# samples. VB is case-insensitive,
' which is why the generator never distinguishes two names by casing alone.

Imports PricingRules

Module Program

    Private ReadOnly Catalogue As String() = {"widget", "gadget"}

    Sub Main()
        Dim pricing As IPricingModule = PricingModule.Create()

        Console.WriteLine("VB: 100 less 15% = {0}", pricing.Discount(100.0, 15L))
        Console.WriteLine("VB: tier of 1200 is {0}", pricing.Tier(1200L))
        Console.WriteLine("VB: widget in catalogue: {0}", pricing.InCatalogue("widget"))

        Dim shapes As New List(Of String)
        For Each bundle In pricing.Bundle(Catalogue)
            shapes.Add(String.Join("+", bundle))
        Next
        Console.WriteLine("VB: bundles: {0}", String.Join(", ", shapes))

        ' An out parameter becomes a ByRef argument in VB.
        Dim level As Long
        If pricing.TryStockLevel("widget", level) Then
            Console.WriteLine("VB: widget stock is {0}", level)
        End If
    End Sub

End Module
