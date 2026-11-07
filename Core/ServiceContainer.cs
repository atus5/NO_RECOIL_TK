using System.Diagnostics;
using HackTK.Next.Abstractions;

namespace HackTK.Next.Core;

public sealed class SystemTimeProvider : ITimeProvider
{
    private static readonly long StartTimestamp = Stopwatch.GetTimestamp();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public long MonotonicMilliseconds => Environment.TickCount64;
    public float UnscaledSeconds => (float)Stopwatch.GetElapsedTime(StartTimestamp).TotalSeconds;
}

public sealed class ServiceContainer : IServiceRegistry, IServiceResolver
{
    private sealed class Registration
    {
        public required bool IsSingleton { get; init; }
        public required Func<IServiceResolver, object> Factory { get; init; }
        public object? SingletonInstance { get; set; }
    }

    private readonly object _sync = new();
    private readonly Dictionary<Type, Registration> _registrations = new();
    private readonly List<IDisposable> _ownedDisposables = new();
    private readonly ThreadLocal<HashSet<Type>> _resolutionStack = new(() => new HashSet<Type>());
    private bool _disposed;

    public void RegisterSingleton<TService>(TService instance) where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        lock (_sync)
        {
            ThrowIfDisposed();
            _registrations[typeof(TService)] = new Registration
            {
                IsSingleton = true,
                Factory = _ => instance,
                SingletonInstance = instance
            };
        }
    }

    public void RegisterSingleton<TService>(Func<IServiceResolver, TService> factory) where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (_sync)
        {
            ThrowIfDisposed();
            _registrations[typeof(TService)] = new Registration
            {
                IsSingleton = true,
                Factory = factory
            };
        }
    }

    public void RegisterTransient<TService>(Func<IServiceResolver, TService> factory) where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (_sync)
        {
            ThrowIfDisposed();
            _registrations[typeof(TService)] = new Registration
            {
                IsSingleton = false,
                Factory = factory
            };
        }
    }

    public TService Resolve<TService>() where TService : class
    {
        if (TryResolve<TService>(out var instance) && instance is not null)
            return instance;

        throw new InvalidOperationException($"Chưa đăng ký dịch vụ kiểu '{typeof(TService).FullName}' trong ServiceContainer.");
    }

    public bool TryResolve<TService>(out TService? service) where TService : class
    {
        Type serviceType = typeof(TService);
        if (serviceType == typeof(IServiceResolver) || serviceType == typeof(IServiceRegistry))
        {
            service = (this as TService)!;
            return true;
        }

        Registration? reg;
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!_registrations.TryGetValue(serviceType, out reg))
            {
                service = null;
                return false;
            }

            if (reg.IsSingleton && reg.SingletonInstance is TService cached)
            {
                service = cached;
                return true;
            }
        }

        var stack = _resolutionStack.Value!;
        if (!stack.Add(serviceType))
            throw new InvalidOperationException($"Phát hiện phụ thuộc vòng (circular dependency) khi khởi tạo '{serviceType.FullName}'.");

        try
        {
            if (reg.IsSingleton)
            {
                lock (_sync)
                {
                    ThrowIfDisposed();
                    if (reg.SingletonInstance is TService existing)
                    {
                        service = existing;
                        return true;
                    }

                    var created = (TService)reg.Factory(this);
                    reg.SingletonInstance = created;
                    if (created is IDisposable disposable && !ReferenceEquals(created, this))
                        _ownedDisposables.Add(disposable);
                    service = created;
                    return true;
                }
            }
            else
            {
                service = (TService)reg.Factory(this);
                return true;
            }
        }
        finally
        {
            stack.Remove(serviceType);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ServiceContainer));
    }

    public void Dispose()
    {
        List<IDisposable> toDispose;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            toDispose = new List<IDisposable>(_ownedDisposables);
            _ownedDisposables.Clear();
            _registrations.Clear();
        }

        for (int i = toDispose.Count - 1; i >= 0; i--)
        {
            try { toDispose[i].Dispose(); }
            catch { /* Isolate disposal faults across independent singletons */ }
        }

        _resolutionStack.Dispose();
    }
}
