using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

/// <summary>Counts consecutive element steps that visibly changed nothing. <c>Unchanged</c> increments,
/// any other verified effect resets, <c>NotVerified</c> says nothing and leaves the count alone.</summary>
internal sealed class StallTracker(int limit = 3)
{
    private int _count;

    /// <summary>Records one element step's effect; true once <paramref name="limit"/> unchanged steps in a row are reached.</summary>
    public bool Record(ActionEffect e)
    {
        switch (e)
        {
            case ActionEffect.Unchanged:
                _count++;
                break;
            case ActionEffect.Changed or ActionEffect.ValueVerified or ActionEffect.ValueMismatch:
                _count = 0;
                break;
        }

        return _count >= limit;
    }
}
