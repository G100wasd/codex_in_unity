using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEditor;

/// Transfers work received by the HTTP listener onto Unity's Editor main thread.
[InitializeOnLoad]
internal static class CodexUnityEditorDispatcher
{
    private static readonly ConcurrentQueue<Action> Pending = new ConcurrentQueue<Action>();

    static CodexUnityEditorDispatcher()
    {
        EditorApplication.update += Drain;
    }

    internal static Task<T> RunAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>();
        Pending.Enqueue(() =>
        {
            try { completion.TrySetResult(action()); }
            catch (Exception error) { completion.TrySetException(error); }
        });
        return completion.Task;
    }

    private static void Drain()
    {
        var processed = 0;
        while (processed++ < 32 && Pending.TryDequeue(out var action)) action();
    }
}
