//-----------------------------------------------------------------------
// <copyright file="Expiration.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
extern alias database;
using System;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Reflection;
using System.Threading;
using Raven.Abstractions.Data;
using Raven.Bundles.Encryption.Plugin;
using Raven.Bundles.Expiration;
using Raven.Bundles.Tests.Versioning;
using Raven.Client.Document;
using Raven.Json.Linq;
using Raven.Server;
using Xunit;
using System.Linq;

namespace Raven.Bundles.Tests.Encryption
{
	public abstract class Encryption : IDisposable
	{
		private readonly string path;
		protected readonly DocumentStore documentStore;
		private readonly RavenDbServer ravenDbServer;

		public Encryption()
		{
			path = Path.GetDirectoryName(Assembly.GetAssembly(typeof(Versioning.Versioning)).CodeBase);
			path = Path.Combine(path, "TestDb").Substring(6);
			database::Raven.Database.Extensions.IOExtensions.DeleteDirectory(path);
			ravenDbServer = new RavenDbServer(
				new database::Raven.Database.Config.RavenConfiguration
				{
					Port = 8079,
					RunInUnreliableYetFastModeThatIsNotSuitableForProduction = true,
					DataDirectory = path,
					Catalog =
						{
							Catalogs =
								{
									new AssemblyCatalog(typeof (DocumentEncryption).Assembly)
								},
						},
					Settings =
						{
							{"Raven/Encryption/Key", "3w17MIVIBLSWZpzH0YarqRlR2+yHiv1Zq3TCWXLEMI8="}
						}
				});
			documentStore = new DocumentStore
			{
				Url = "http://localhost:8079"
			};
			documentStore.Initialize();
		}

		public void Dispose()
		{
			documentStore.Dispose();
			ravenDbServer.Dispose();
			database::Raven.Database.Extensions.IOExtensions.DeleteDirectory(path);
		}
	}
}