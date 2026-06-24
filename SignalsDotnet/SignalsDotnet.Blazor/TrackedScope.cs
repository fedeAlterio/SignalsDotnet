using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using R3;

namespace SignalsDotnet.Blazor;

public class TrackedScope : ComponentBase, IDisposable
{
    private readonly SerialDisposable _disposable = new();
    private SynchronizationContext? _syncContext;

    [Parameter, EditorRequired]
    public RenderFragment ChildContent { get; set; } = null!;

    protected override void OnInitialized() => _syncContext = SynchronizationContext.Current;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        using (Signal.UntrackedScope())
        {
            // Reentrancy guard
            var oldDisposable = _disposable.Disposable;
            using (Signal.TrackedScope(out var subscription, OnSignalChanged))
            {
                if (Equals(_disposable.Disposable, oldDisposable))
                {
                    _disposable.Disposable = subscription;
                    builder.AddContent(0, ChildContent);
                }
                else
                {
                    subscription.Dispose();
                }
            }
        }
    }

    private void OnSignalChanged()
    {
        if (SynchronizationContext.Current == _syncContext)
        {
            StateHasChanged();
            return;
        }

        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose() => _disposable.Dispose();
}