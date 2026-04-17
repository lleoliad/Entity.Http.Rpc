using Fantasy;
using Fantasy.Async;
using Microsoft.Extensions.Logging;

namespace Entities.Http.Rpc;

internal static class HttpProtoSceneDispatcher
{
    public static Task RunAsync(Scene scene, Func<FTask> action)
    {
        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        scene.ThreadSynchronizationContext.Post(() => Execute(action, completionSource).Coroutine());

        return completionSource.Task;
    }

    public static Task<T> RunAsync<T>(Scene scene, Func<FTask<T>> action)
    {
        var completionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        scene.ThreadSynchronizationContext.Post(() => Execute(action, completionSource).Coroutine());

        return completionSource.Task;
    }

    private static async FTask Execute(Func<FTask> action, TaskCompletionSource completionSource)
    {
        try
        {
            await action();
            completionSource.TrySetResult();
        }
        catch (Exception exception)
        {
            completionSource.TrySetException(exception);
        }
    }

    private static async FTask Execute<T>(Func<FTask<T>> action, TaskCompletionSource<T> completionSource)
    {
        try
        {
            var result = await action();
            completionSource.TrySetResult(result);
        }
        catch (Exception exception)
        {
            completionSource.TrySetException(exception);
        }
    }
}
