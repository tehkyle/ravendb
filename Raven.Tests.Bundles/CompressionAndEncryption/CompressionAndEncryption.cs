//-----------------------------------------------------------------------
// <copyright file="Expiration.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.IO;
using System.Reflection;
using Raven.Client.Document;
using Raven.Server;
using Raven.Tests.Common.Util;

namespace Raven.Tests.Bundles.CompressionAndEncryption
{
    public abstract class CompressionAndEncryption : IDisposable
    {
        protected readonly string path;
        protected readonly DocumentStore documentStore;
        private RavenDbServer ravenDbServer;
        private bool closed = false;
        private Raven.Database.Config.RavenConfiguration settings;

        protected CompressionAndEncryption()
        {
            path = Path.GetDirectoryName(Assembly.GetAssembly(typeof(Versioning.Versioning)).CodeBase);
            path = Path.Combine(path, "TestDb").Substring(6);
            Raven.Database.Extensions.IOExtensions.DeleteDirectory(path);
            settings = new Raven.Database.Config.RavenConfiguration
            {
                Port = 8079,
                RunInUnreliableYetFastModeThatIsNotSuitableForProduction = true,
                MaxSecondsForTaskToWaitForDatabaseToLoad = 20,
                DataDirectory = path,
                Settings =
                    {
                        {"Raven/Encryption/Key", "3w17MIVIBLSWZpzH0YarqRlR2+yHiv1Zq3TCWXLEMI8="},
                        {"Raven/ActiveBundles", "Compression;Encryption"}
                    }
            };
            ConfigureServer(settings);
            settings.PostInit();
            ravenDbServer = new RavenDbServer(settings)
            {
                UseEmbeddedHttpServer = true
            };
            ravenDbServer.Initialize();
            documentStore = new DocumentStore
            {
                Url = "http://localhost:8079"
            };
            documentStore.Initialize();
        }

        protected virtual void ConfigureServer(Raven.Database.Config.RavenConfiguration ravenConfiguration)
        {
        }

        protected void AssertPlainTextIsNotSavedInDatabase(params string[] plaintext)
        {
            Close();
            EncryptionTestUtil.AssertPlainTextIsNotSavedInAnyFileInPath(plaintext, path, s => true);
        }

        protected void RecycleServer()
        {
            ravenDbServer.Dispose();
            ravenDbServer =  new RavenDbServer(settings)
            {
                UseEmbeddedHttpServer = true
            };
            ravenDbServer.Initialize();
        }

        protected void Close()
        {
            if (closed)
                return;

            documentStore.Dispose();
            ravenDbServer.Dispose();
            closed = true;
        }

        public void Dispose()
        {
            Close();
            Raven.Database.Extensions.IOExtensions.DeleteDirectory(path);
        }
    }
}
