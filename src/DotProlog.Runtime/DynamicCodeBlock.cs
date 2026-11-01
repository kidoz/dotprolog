namespace DotProlog.Runtime;

// Only private, unpublished compiler output may acquire this ownership. Public code addresses
// and constants remain pinned. The clause's logical-update generation protects both ranges.
internal readonly record struct DynamicCodeBlock(int CodeStart, int CodeLength, int ConstantStart, int ConstantCount)
{
    internal bool IsAllocated => CodeLength != 0;
}
