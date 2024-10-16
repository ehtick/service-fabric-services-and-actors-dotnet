// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Microsoft.ServiceFabric.Services.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Fabric.Health;
    using System.Threading;
    using System.Threading.Tasks;
    using FluentAssertions;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;
    using Moq;
    using Xunit;

    /// <summary>
    /// Class for StatelessService lifecycle tests.
    /// </summary>
    public class StatelessServiceLifeCycleTests
    {
        /// <summary>
        /// Tests RunAsync blocking call.
        /// </summary>
        [Fact]
        public async Task RunAsyncBlockingCall()
        {
            Console.WriteLine("StatelessServiceLifeCycleTests - Test Method: RunAsyncBlockingCall()");

            var serviceContext = TestMocksRepository.GetMockStatelessServiceContext();

            var testService = new RunAsyncBlockingCallTestService(serviceContext);
            IStatelessServiceInstance testServiceReplica = new StatelessServiceInstanceAdapter(serviceContext, testService);

            var partition = new Mock<IStatelessServicePartition>();

            var openTask = testServiceReplica.OpenAsync(partition.Object, CancellationToken.None);

            var source = new CancellationTokenSource(10000);
            while (!testService.RunAsyncInvoked)
            {
                await Task.Delay(100, source.Token);
            }

            Assert.True(openTask.IsCompleted && !openTask.IsCanceled && !openTask.IsFaulted);
            ((StatelessServiceInstanceAdapter)testServiceReplica).Test_IsRunAsyncTaskRunning().Should().BeTrue();

            await testServiceReplica.CloseAsync(CancellationToken.None);
        }

        /// <summary>
        /// Tests RunAsync cancellation.
        /// </summary>
        [Fact]
        public async Task RunAsyncCancellation()
        {
            Console.WriteLine("StatelessServiceLifeCycleTests - Test Method: RunAsyncCancellation()");

            var serviceContext = TestMocksRepository.GetMockStatelessServiceContext();

            var testService = new RunAsyncCancellationTestService(serviceContext);
            IStatelessServiceInstance testServiceReplica = new StatelessServiceInstanceAdapter(serviceContext, testService);

            var partition = new Mock<IStatelessServicePartition>();
            await testServiceReplica.OpenAsync(partition.Object, CancellationToken.None);

            var source = new CancellationTokenSource(10000);
            while (!testService.StartedWaiting)
            {
                await Task.Delay(100, source.Token);
            }

            // This will throw if cancellation propagates out, as canceling of RunAsync is
            // awaited during close of stateless serice instance.
            await testServiceReplica.CloseAsync(CancellationToken.None);

            partition.Verify(p => p.ReportFault(It.IsAny<FaultType>()), Times.Never());
        }

        /// <summary>
        /// Tests slow cancellation of RunAsync.
        /// </summary>
        [Fact]
        public async Task RunAsyncSlowCancellation()
        {
            Console.WriteLine("StatelessServiceLifeCycleTests - Test Method: RunAsyncSlowCancellation()");

            var serviceContext = TestMocksRepository.GetMockStatelessServiceContext();

            var testService = new RunAsyncSlowCancellationTestService(serviceContext);
            IStatelessServiceInstance testServiceReplica = new StatelessServiceInstanceAdapter(serviceContext, testService);

            var partition = new Mock<IStatelessServicePartition>();
            partition.Setup(p => p.ReportPartitionHealth(It.IsAny<HealthInformation>()));

            await testServiceReplica.OpenAsync(partition.Object, CancellationToken.None);

            var source = new CancellationTokenSource(10000);
            while (!testService.RunAsyncInvoked)
            {
                await Task.Delay(100, source.Token);
            }

            await testServiceReplica.CloseAsync(CancellationToken.None);

            partition.Verify(p => p.ReportFault(It.IsAny<FaultType>()), Times.Never());
            partition.Verify(p => p.ReportPartitionHealth(It.Is<HealthInformation>(hinfo => Utility.IsRunAsyncSlowCancellationHealthInformation(hinfo))), Times.AtLeastOnce);
        }

        /// <summary>
        /// Tests exceptions from RunAsync.
        /// </summary>
        [Fact]
        public async Task RunAsyncFail()
        {
            Console.WriteLine("StatelessServiceLifeCycleTests - Test Method: RunAsyncFail()");

            var serviceContext = TestMocksRepository.GetMockStatelessServiceContext();

            IStatelessServiceInstance testServiceReplica = new StatelessServiceInstanceAdapter(serviceContext, new RunAsyncFailTestService(serviceContext));

            var tcs = new TaskCompletionSource<bool>();
            var partition = new Mock<IStatelessServicePartition>();
            partition.Setup(p => p.ReportFault(It.IsAny<FaultType>())).Callback(
                () =>
                {
                    tcs.SetResult(true);
                });

            partition.Setup(p => p.ReportPartitionHealth(It.IsAny<HealthInformation>()));

            await testServiceReplica.OpenAsync(partition.Object, CancellationToken.None);

            var dontWait = Task.Run(
                async () =>
                {
                    await Task.Delay(10000);
                    tcs.SetCanceled();
                });

            (await tcs.Task).Should().BeTrue();

            // This will throw if RunAsync exception propagates out
            await testServiceReplica.CloseAsync(CancellationToken.None);

            partition.Verify(p => p.ReportFault(It.IsNotIn(FaultType.Transient)), Times.Never());
            partition.Verify(p => p.ReportFault(FaultType.Transient), Times.Once());
            partition.Verify(p => p.ReportPartitionHealth(It.Is<HealthInformation>(hinfo => Utility.IsRunAsyncUnhandledExceptionHealthInformation(hinfo))), Times.Once());
        }

        /// <summary>
        /// Tests exception from Listener on Abort.
        /// </summary>
        [Fact]
        public async Task ListenerExceptionOnAbort()
        {
            Console.WriteLine("StatelessServiceLifeCycleTests - Test Method: ListenerExceptionOnAbort()");

            var serviceContext = TestMocksRepository.GetMockStatelessServiceContext();

            IStatelessServiceInstance testServiceInstance =
                new StatelessServiceInstanceAdapter(serviceContext, new ListenerExceptionOnAbortService(serviceContext));

            var partition = new Mock<IStatelessServicePartition>();

            await testServiceInstance.OpenAsync(partition.Object, CancellationToken.None);

            // This will throw if listener exception propagates out
            testServiceInstance.Abort();
        }

        /// <summary>
        /// Tests when null service instance listeners is returned.
        /// </summary>
        [Fact]
        public async Task NullListener()
        {
            Console.WriteLine("StatelessServiceLifeCycleTests - Test Method: NullListener()");

            var serviceContext = TestMocksRepository.GetMockStatelessServiceContext();

            IStatelessServiceInstance testServiceInstance =
                new StatelessServiceInstanceAdapter(serviceContext, new NullListenerService(serviceContext));

            var partition = new Mock<IStatelessServicePartition>();

            // No exception should be thrown
            await testServiceInstance.OpenAsync(partition.Object, CancellationToken.None);
        }

        /// <summary>
        /// Tests when null communication listener is returned.
        /// </summary>
        [Fact]
        public async Task NullCommunicationListener()
        {
            Console.WriteLine("StatelessServiceLifeCycleTests - Test Method: NullCommunicationListener()");

            var serviceContext = TestMocksRepository.GetMockStatelessServiceContext();

            IStatelessServiceInstance testServiceInstance =
                new StatelessServiceInstanceAdapter(serviceContext, new NullCommunicationListenerService(serviceContext));

            var partition = new Mock<IStatelessServicePartition>();

            // No exception should be thrown
            await testServiceInstance.OpenAsync(partition.Object, CancellationToken.None);
        }

        private class RunAsyncBlockingCallTestService : StatelessService
        {
            public RunAsyncBlockingCallTestService(StatelessServiceContext context)
                : base(context)
            {
                this.RunAsyncInvoked = false;
            }

            public bool RunAsyncInvoked { get; private set; }

            protected override Task RunAsync(CancellationToken cancellationToken)
            {
                this.RunAsyncInvoked = true;

                long i = 0;
                while (!cancellationToken.IsCancellationRequested)
                {
                    i++;
                }

                return Task.FromResult(i);
            }
        }

        private class RunAsyncCancellationTestService : StatelessService
        {
            private const int ToWait = 100;

            public RunAsyncCancellationTestService(StatelessServiceContext context)
                : base(context)
            {
            }

            public bool StartedWaiting { get; internal set; } = false;

            protected override async Task RunAsync(CancellationToken cancellationToken)
            {
                this.StartedWaiting = true;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(ToWait, cancellationToken);
                }
            }
        }

        private class ListenerExceptionOnAbortService : StatelessService
        {
            public ListenerExceptionOnAbortService(StatelessServiceContext context)
                : base(context)
            {
            }

            protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
            {
                return new[]
                {
                    new ServiceInstanceListener(this.CreateCommunicationListener),
                };
            }

            private ICommunicationListener CreateCommunicationListener(ServiceContext context)
            {
                Console.WriteLine("Creating listener");
                var mockListener = new Mock<ICommunicationListener>();
                mockListener.Setup(x => x.OpenAsync(It.IsAny<CancellationToken>())).Returns(Task.FromResult("Address"));
                mockListener.Setup(x => x.Abort()).Throws(new Exception("Listener Abort exception."));

                return mockListener.Object;
            }
        }

        private class RunAsyncFailTestService : StatelessService
        {
            public RunAsyncFailTestService(StatelessServiceContext context)
                : base(context)
            {
            }

            protected override Task RunAsync(CancellationToken cancellationToken)
            {
                return Task.Run(() => { throw new FabricException(); }, CancellationToken.None);
            }
        }

        private class RunAsyncSlowCancellationTestService : StatelessService
        {
            public RunAsyncSlowCancellationTestService(StatelessServiceContext context)
                : base(context)
            {
                this.RunAsyncInvoked = false;
            }

            public bool RunAsyncInvoked { get; private set; }

            protected override async Task RunAsync(CancellationToken cancellationToken)
            {
                this.RunAsyncInvoked = true;
                await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None);
            }
        }

        private class NullListenerService : StatelessService
        {
            public NullListenerService(StatelessServiceContext context)
                : base(context)
            {
            }

            protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
            {
                return new ServiceInstanceListener[]
                {
                    null,
                };
            }
        }

        private class NullCommunicationListenerService : StatelessService
        {
            public NullCommunicationListenerService(StatelessServiceContext context)
                : base(context)
            {
            }

            protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
            {
                return new[]
                {
                    new ServiceInstanceListener(this.CreateCommunicationListener),
                };
            }

            private ICommunicationListener CreateCommunicationListener(ServiceContext context)
            {
                return null;
            }
        }
    }
}
