namespace HackTK.Next.Services;

public sealed class TransactionalValueOwnership<TTarget, TValue> where TTarget : class
{
    private readonly Func<TTarget, TValue> _getter;
    private readonly Action<TTarget, TValue> _setter;
    private readonly IEqualityComparer<TValue> _comparer;
    private TTarget? _owner;
    private TValue _originalValue = default!;
    private TValue _appliedValue = default!;

    public TransactionalValueOwnership(
        Func<TTarget, TValue> getter,
        Action<TTarget, TValue> setter,
        IEqualityComparer<TValue>? comparer = null)
    {
        _getter = getter ?? throw new ArgumentNullException(nameof(getter));
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        _comparer = comparer ?? EqualityComparer<TValue>.Default;
    }

    public bool HasActiveOwner => _owner is not null;
    public TValue? OriginalValue => _owner is null ? default : _originalValue;
    public TValue? CurrentValue => _owner is null ? default : _getter(_owner);

    public void Apply(TTarget target, TValue desiredValue)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!ReferenceEquals(target, _owner))
        {
            Restore();
            _originalValue = _getter(target);
            _owner = target;
            _appliedValue = desiredValue;
        }
        else
        {
            TValue current = _getter(_owner);
            if (!_comparer.Equals(current, _appliedValue))
                _originalValue = current;
        }

        _setter(_owner, desiredValue);
        _appliedValue = desiredValue;
    }

    public TValue? Restore()
    {
        if (_owner is null)
            return default;

        TValue current = _getter(_owner);
        if (_comparer.Equals(current, _appliedValue))
            _setter(_owner, _originalValue);

        TValue finalValue = _getter(_owner);
        _owner = null;
        return finalValue;
    }
}

public static class MaterialSlotDiffService
{
    public static TMaterial?[] CaptureOriginalSlots<TMaterial>(
        IReadOnlyList<TMaterial?> currentSlots,
        IReadOnlyList<TMaterial?> previousOriginalSlots,
        TMaterial ownedReplacement) where TMaterial : class
    {
        ArgumentNullException.ThrowIfNull(currentSlots);
        ArgumentNullException.ThrowIfNull(previousOriginalSlots);
        ArgumentNullException.ThrowIfNull(ownedReplacement);

        var result = new TMaterial?[currentSlots.Count];
        for (int i = 0; i < currentSlots.Count; i++)
        {
            result[i] = ReferenceEquals(currentSlots[i], ownedReplacement)
                ? (i < previousOriginalSlots.Count ? previousOriginalSlots[i] : null)
                : currentSlots[i];
        }

        return result;
    }
}
