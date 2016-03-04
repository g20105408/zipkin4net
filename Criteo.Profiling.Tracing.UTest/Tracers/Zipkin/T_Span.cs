﻿using System;
using System.Net;
using Criteo.Profiling.Tracing.Tracers.Zipkin;
using Criteo.Profiling.Tracing.Tracers.Zipkin.Thrift;
using Criteo.Profiling.Tracing.Utils;
using NUnit.Framework;
using BinaryAnnotation = Criteo.Profiling.Tracing.Tracers.Zipkin.BinaryAnnotation;
using Span = Criteo.Profiling.Tracing.Tracers.Zipkin.Span;
using ThriftSpan = Criteo.Profiling.Tracing.Tracers.Zipkin.Thrift.Span;

namespace Criteo.Profiling.Tracing.UTest.Tracers.Zipkin
{
    [TestFixture]
    class T_Span
    {
        private const string SomeRandomAnnotation = "SomethingHappenedHere";

        [Test]
        [Description("Span should only be marked as complete when either ClientRecv or ServerSend are present.")]
        public void SpansAreLabeledAsCompleteWhenCrOrSs()
        {
            var spanState = new SpanState(1, 0, 2, Flags.Empty);
            var started = TimeUtils.UtcNow;

            var spanClientRecv = new Span(spanState, started);
            Assert.False(spanClientRecv.Complete);

            spanClientRecv.AddAnnotation(new ZipkinAnnotation(TimeUtils.UtcNow, zipkinCoreConstants.CLIENT_RECV));
            Assert.True(spanClientRecv.Complete);

            var spanServSend = new Span(spanState, started);
            Assert.False(spanServSend.Complete);

            spanServSend.AddAnnotation(new ZipkinAnnotation(TimeUtils.UtcNow, zipkinCoreConstants.SERVER_SEND));
            Assert.True(spanServSend.Complete);


            var spanOtherAnn = new Span(spanState, started);
            Assert.False(spanOtherAnn.Complete);

            spanServSend.AddAnnotation(new ZipkinAnnotation(TimeUtils.UtcNow, zipkinCoreConstants.SERVER_RECV));
            spanServSend.AddAnnotation(new ZipkinAnnotation(TimeUtils.UtcNow, zipkinCoreConstants.CLIENT_SEND));
            Assert.False(spanOtherAnn.Complete);
        }

        [TestCase(null)]
        [TestCase(123456L)]
        public void SpanCorrectlyConvertedToThrift(long? parentSpanId)
        {
            var hostIp = IPAddress.Loopback;
            const int hostPort = 1234;
            const string serviceName = "myCriteoService";
            const string methodName = "GET";

            var spanState = new SpanState(1, parentSpanId, 2, Flags.Empty);
            var span = new Span(spanState, TimeUtils.UtcNow) { Endpoint = new IPEndPoint(hostIp, hostPort), ServiceName = serviceName, Name = methodName };

            var zipkinAnnDateTime = TimeUtils.UtcNow;
            AddClientSendReceiveAnnotations(span, zipkinAnnDateTime);
            span.AddAnnotation(new ZipkinAnnotation(zipkinAnnDateTime, SomeRandomAnnotation));

            const string binAnnKey = "http.uri";
            var binAnnVal = new byte[] { 0x00 };
            const AnnotationType binAnnType = AnnotationType.STRING;

            span.AddBinaryAnnotation(new BinaryAnnotation(binAnnKey, binAnnVal, binAnnType));

            var thriftSpan = span.ToThrift();

            var expectedHost = new Endpoint()
            {
                Ipv4 = Span.IpToInt(hostIp),
                Port = hostPort,
                Service_name = serviceName
            };

            Assert.AreEqual(1, thriftSpan.Trace_id);
            Assert.AreEqual(2, thriftSpan.Id);

            if (span.IsRoot)
            {
                Assert.IsNull(thriftSpan.Parent_id); // root span has no parent
            }
            else
            {
                Assert.AreEqual(parentSpanId, thriftSpan.Parent_id);
            }

            Assert.AreEqual(false, thriftSpan.Debug);
            Assert.AreEqual(methodName, thriftSpan.Name);

            Assert.AreEqual(3, thriftSpan.Annotations.Count);

            thriftSpan.Annotations.ForEach(ann =>
            {
                Assert.AreEqual(expectedHost, ann.Host);
                Assert.AreEqual(TimeUtils.ToUnixTimestamp(zipkinAnnDateTime), ann.Timestamp);
            });

            Assert.AreEqual(1, thriftSpan.Binary_annotations.Count);

            thriftSpan.Binary_annotations.ForEach(ann =>
            {
                Assert.AreEqual(expectedHost, ann.Host);
                Assert.AreEqual(binAnnKey, ann.Key);
                Assert.AreEqual(binAnnVal, ann.Value);
                Assert.AreEqual(binAnnType, ann.Annotation_type);
            });
        }

        [Test]
        [Description("Span should never be sent without required fields such as Name, ServiceName, Ipv4 or Port")]
        public void DefaultsValuesAreUsedIfNothingSpecified()
        {
            var spanState = new SpanState(1, 0, 2, Flags.Empty);
            var span = new Span(spanState, TimeUtils.UtcNow);
            AddClientSendReceiveAnnotations(span);

            var thriftSpan = span.ToThrift();
            AssertSpanHasRequiredFields(thriftSpan);

            const string defaultName = Span.DefaultRpcMethod;
            var defaultServiceName = TraceManager.Configuration.DefaultServiceName;
            var defaultIpv4 = Span.IpToInt(TraceManager.Configuration.DefaultEndPoint.Address);
            var defaultPort = TraceManager.Configuration.DefaultEndPoint.Port;

            Assert.AreEqual(2, thriftSpan.Annotations.Count);
            thriftSpan.Annotations.ForEach(ann =>
            {
                Assert.AreEqual(defaultServiceName, ann.Host.Service_name);
                Assert.AreEqual(defaultIpv4, ann.Host.Ipv4);
                Assert.AreEqual(defaultPort, ann.Host.Port);
            });

            Assert.AreEqual(defaultName, thriftSpan.Name);
        }

        [Test]
        public void DefaultsValuesAreNotUsedIfValuesSpecified()
        {
            var spanState = new SpanState(1, 0, 2, Flags.Empty);
            var started = TimeUtils.UtcNow;

            // Make sure we choose something different thant the default values
            var serviceName = TraceManager.Configuration.DefaultServiceName + "_notDefault";
            var hostPort = TraceManager.Configuration.DefaultEndPoint.Port + 1;

            const string name = "myRPCmethod";

            var span = new Span(spanState, started) { Endpoint = new IPEndPoint(IPAddress.Loopback, hostPort), ServiceName = serviceName, Name = name };
            AddClientSendReceiveAnnotations(span);

            var thriftSpan = span.ToThrift();
            AssertSpanHasRequiredFields(thriftSpan);

            Assert.NotNull(thriftSpan);
            Assert.AreEqual(2, thriftSpan.Annotations.Count);

            thriftSpan.Annotations.ForEach(annotation =>
            {
                Assert.AreEqual(serviceName, annotation.Host.Service_name);
                Assert.AreEqual(Span.IpToInt(IPAddress.Loopback), annotation.Host.Ipv4);
                Assert.AreEqual(hostPort, annotation.Host.Port);
            });

            Assert.AreEqual(name, thriftSpan.Name);
        }

        [Test]
        public void IpToIntConversionIsCorrect()
        {
            const string ipStr = "192.168.1.56";
            const int expectedIp = unchecked((int)3232235832);

            var ipAddr = IPAddress.Parse(ipStr);

            var ipInt = Span.IpToInt(ipAddr);

            Assert.AreEqual(expectedIp, ipInt);
        }

        [TestCase(null)]
        [TestCase(123456L)]
        public void RootSpanPropertyIsCorrect(long? parentSpanId)
        {
            var spanState = new SpanState(1, parentSpanId, 1, Flags.Empty);
            var span = new Span(spanState, TimeUtils.UtcNow);

            Assert.AreEqual(parentSpanId == null, span.IsRoot);
        }

        [Test]
        public void WhiteSpacesAreRemovedFromServiceName()
        {
            var spanState = new SpanState(1, 0, 2, Flags.Empty);
            var span = new Span(spanState, TimeUtils.UtcNow) { ServiceName = "my Criteo Service" };
            AddClientSendReceiveAnnotations(span);

            var thriftSpan = span.ToThrift();

            Assert.AreEqual("my_Criteo_Service", thriftSpan.Annotations[0].Host.Service_name);
        }

        private static void AddClientSendReceiveAnnotations(Span span)
        {
            AddClientSendReceiveAnnotations(span, TimeUtils.UtcNow);
        }

        private static void AddClientSendReceiveAnnotations(Span span, DateTime dateTime)
        {
            span.AddAnnotation(new ZipkinAnnotation(dateTime, zipkinCoreConstants.CLIENT_SEND));
            span.AddAnnotation(new ZipkinAnnotation(dateTime, zipkinCoreConstants.CLIENT_RECV));
        }

        private static void AssertSpanHasRequiredFields(ThriftSpan thriftSpan)
        {
            Assert.IsNotNull(thriftSpan.Id);
            Assert.IsNotNull(thriftSpan.Trace_id);
            Assert.IsNotNullOrEmpty(thriftSpan.Name);

            thriftSpan.Annotations.ForEach(annotation =>
            {
                Assert.IsNotNullOrEmpty(annotation.Host.Service_name);
                Assert.IsNotNull(annotation.Host.Ipv4);
                Assert.IsNotNull(annotation.Host.Port);

                Assert.IsNotNull(annotation.Timestamp);
                Assert.That(annotation.Timestamp, Is.GreaterThan(0));
                Assert.IsNotNullOrEmpty(annotation.Value);
            });

            if (thriftSpan.Binary_annotations != null)
            {
                thriftSpan.Binary_annotations.ForEach(annotation =>
                {
                    Assert.IsNotNullOrEmpty(annotation.Host.Service_name);
                    Assert.IsNotNull(annotation.Host.Ipv4);
                    Assert.IsNotNull(annotation.Host.Port);

                    Assert.IsNotNull(annotation.Annotation_type);
                    Assert.IsNotNull(annotation.Value);
                });
            }
        }

    }
}
