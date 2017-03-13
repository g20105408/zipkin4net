using System;
using Microsoft.AspNetCore.Builder;
using Criteo.Profiling.Tracing;

namespace Criteo.Profiling.Tracing.Middleware
{
    public static class TracingMiddleware
    {
        public static void UseTracing(this IApplicationBuilder app, string serviceName)
        {
            app.Use(async (context, next) => {
                var trace = Trace.Create();
                Trace.Current = trace;
                trace.Record(Annotations.ServerRecv());
                trace.Record(Annotations.ServiceName(serviceName));
                trace.Record(Annotations.Rpc(context.Request.Method));
                await next.Invoke();
                trace.Record(Annotations.ServerSend());
            });
        }
    }
}