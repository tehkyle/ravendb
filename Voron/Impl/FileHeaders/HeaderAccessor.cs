﻿// -----------------------------------------------------------------------
//  <copyright file="HeaderAccessor.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Voron.Impl.FileHeaders
{
	public unsafe delegate void ModifyHeaderAction(FileHeader* ptr);

	public unsafe delegate T GetDataFromHeaderAction<T>(FileHeader* ptr);
	
	public unsafe class HeaderAccessor : IDisposable
	{
		private readonly StorageEnvironment _env;

		private readonly ReaderWriterLockSlim _locker = new ReaderWriterLockSlim();
		private long _revision = -1;

		private readonly FileHeader* _theHeader;
		private IntPtr _headerPtr;

		private static string[] _headerFileNames = new[]
		{
			"headers.one",
			"headers.two"
		};

		public HeaderAccessor(StorageEnvironment env)
		{
			this._env = env;
			
			_headerPtr = Marshal.AllocHGlobal(sizeof(FileHeader));
			_theHeader = (FileHeader*)_headerPtr.ToPointer();
		}

		public bool Initialize()
		{
			var headers = stackalloc FileHeader[2];
			var f1 = headers;
			var f2 = headers + sizeof(FileHeader);
			if (_env.Options.ReadHeader(_headerFileNames[0], f1) == false &&
				_env.Options.ReadHeader(_headerFileNames[1], f2) == false)
			{
				// new 
				FillInEmptyHeader(f1);
				_env.Options.WriteHeader(_headerFileNames[0], f1);
				_env.Options.WriteHeader(_headerFileNames[1], f2);

				return true; // new
			}

			if (f1->MagicMarker != Constants.MagicMarker && f2->MagicMarker != Constants.MagicMarker)
				throw new InvalidDataException("None of the header files start with the magic marker, probably not db files");

			// if one of the files is corrupted, but the other isn't, restore to the valid file
			if (f1->MagicMarker != Constants.MagicMarker)
			{
				*f1 = *f2;
			}
			if (f2->MagicMarker != Constants.MagicMarker)
			{
				*f2 = *f1;
			}

			if (f1->Version != Constants.CurrentVersion)
				throw new InvalidDataException("This is a db file for version " + f1->Version + ", which is not compatible with the current version " + Constants.CurrentVersion);

			if (f1->TransactionId < 0)
				throw new InvalidDataException("The transaction number cannot be negative");


			if (f1->HeaderRevision > f2->HeaderRevision)
			{
				*_theHeader = *f1;
			}
			else
			{
				*_theHeader = *f2;
			}
			_revision = _theHeader->HeaderRevision;
			return false;
		}


		public FileHeader CopyHeader()
		{
			_locker.EnterReadLock();
			try
			{
				return *_theHeader;
			}
			finally
			{
				_locker.ExitReadLock();
			}
		}


		public T Get<T>(GetDataFromHeaderAction<T> action)
		{
			_locker.EnterReadLock();
			try
			{
				return action(_theHeader);
			}
			finally
			{
				_locker.ExitReadLock();
			}
		}

		public void Modify(ModifyHeaderAction modifyAction)
		{
			_locker.EnterWriteLock();
			try
			{

				modifyAction(_theHeader);

				_revision++;
				_theHeader->HeaderRevision = _revision;

				var file = _headerFileNames[_revision & 1];

				_env.Options.WriteHeader(file, _theHeader);

			}
			finally
			{
				_locker.ExitWriteLock();
			}
		}

		private static void FillInEmptyHeader(FileHeader* header)
		{
			header->MagicMarker = Constants.MagicMarker;
			header->Version = Constants.CurrentVersion;
			header->HeaderRevision = -1;
			header->TransactionId = 0;
			header->LastPageNumber = 1;
			header->FreeSpace.RootPageNumber = -1;
			header->Root.RootPageNumber = -1;
			header->Journal.CurrentJournal = -1;
			header->Journal.JournalFilesCount = 0;
			header->Journal.LastSyncedJournal = -1;
			header->Journal.LastSyncedJournalPage = -1;
			header->IncrementalBackup.LastBackedUpJournal = -1;
			header->IncrementalBackup.LastBackedUpJournalPage = -1;
			header->IncrementalBackup.LastCreatedJournal = -1;
		}

		public void Dispose()
		{
			if (_headerPtr != IntPtr.Zero)
			{
				Marshal.FreeHGlobal(_headerPtr);
				_headerPtr = IntPtr.Zero;
			}
		}
	}
}