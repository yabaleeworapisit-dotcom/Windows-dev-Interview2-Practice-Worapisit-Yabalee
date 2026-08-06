using NasaApodApp.Models;

namespace NasaApodLib;

// Keeps track of which day in the fetched month is currently on screen.
//
// The slide position is held here rather than in the view model so the stepping rules —
// where the ends are, what an out-of-range jump does, whether stepping past the last day
// wraps — are one small piece of logic that can be reasoned about on its own.
//
// Movement clamps at both ends instead of wrapping: a month is a line of days, not a loop,
// and silently jumping from the 30th back to the 1st reads as a bug to someone browsing.
public sealed class ApodSlideNavigator
{
    private ApodEntry[] slideEntries = [];
    private int currentIndex = -1;

    // The whole deck, for the list beside the slide. Handed out as a read-only view so the
    // list cannot reorder or remove what the navigator is stepping through.
    public IReadOnlyList<ApodEntry> Entries => this.slideEntries;

    public int SlideCount => this.slideEntries.Length;

    public bool HasSlides => this.slideEntries.Length > 0;

    public int CurrentIndex => this.currentIndex;

    // One-based position for display, because "Day 1 of 30" reads better than "Day 0 of 30".
    public int CurrentPosition => this.currentIndex + 1;

    public ApodEntry? CurrentEntry
        => this.currentIndex >= 0 && this.currentIndex < this.slideEntries.Length
            ? this.slideEntries[this.currentIndex]
            : null;

    public bool CanMovePrevious => this.currentIndex > 0;

    public bool CanMoveNext => this.currentIndex >= 0 && this.currentIndex < this.slideEntries.Length - 1;

    // Reaches a slide without moving to it, so the pictures either side of the current one can
    // be fetched ahead of the user asking for them. Out-of-range indexes return null rather
    // than throwing, because the ends of the deck are a normal thing for a caller to walk into.
    public ApodEntry? EntryAt(int Index)
        => Index >= 0 && Index < this.slideEntries.Length ? this.slideEntries[Index] : null;

    public void LoadSlides(ApodEntry[] Entries)
    {
        ArgumentNullException.ThrowIfNull(Entries);

        this.slideEntries = Entries;
        this.currentIndex = Entries.Length > 0 ? 0 : -1;
    }

    // Each mover reports whether the position actually changed, so the caller only reloads
    // the picture when there is a new slide to show.
    public bool MoveNext()
    {
        if (!this.CanMoveNext)
        {
            return false;
        }

        this.currentIndex++;
        return true;
    }

    public bool MovePrevious()
    {
        if (!this.CanMovePrevious)
        {
            return false;
        }

        this.currentIndex--;
        return true;
    }

    public bool MoveFirst() => this.JumpTo(0);

    public bool MoveLast() => this.JumpTo(this.slideEntries.Length - 1);

    // Moves to whichever slide holds this entry. Used by the list: the user picks a day and
    // the navigator follows, rather than the list keeping a position of its own.
    public bool JumpToEntry(ApodEntry? Entry)
    {
        if (Entry is null)
        {
            return false;
        }

        int FoundIndex = Array.IndexOf(this.slideEntries, Entry);

        return this.JumpTo(FoundIndex);
    }

    public bool JumpTo(int TargetIndex)
    {
        if (!this.HasSlides)
        {
            return false;
        }

        if (TargetIndex < 0 || TargetIndex >= this.slideEntries.Length)
        {
            return false;
        }

        if (TargetIndex == this.currentIndex)
        {
            return false;
        }

        this.currentIndex = TargetIndex;
        return true;
    }
}
