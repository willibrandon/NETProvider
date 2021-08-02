﻿/*
 *    The contents of this file are subject to the Initial
 *    Developer's Public License Version 1.0 (the "License");
 *    you may not use this file except in compliance with the
 *    License. You may obtain a copy of the License at
 *    https://github.com/FirebirdSQL/NETProvider/blob/master/license.txt.
 *
 *    Software distributed under the License is distributed on
 *    an "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, either
 *    express or implied. See the License for the specific
 *    language governing rights and limitations under the License.
 *
 *    All Rights Reserved.
 */

//$Authors = Carlos Guzman Alvarez, Jiri Cincura (jiri@cincura.net)

using System;
using System.IO;
using System.Threading.Tasks;
using FirebirdSql.Data.Common;

namespace FirebirdSql.Data.Client.Managed.Version10
{
	internal class GdsServiceManager : ServiceManagerBase
	{
		#region Fields

		private GdsConnection _connection;
		private GdsDatabase _database;

		#endregion

		#region Properties

		public GdsConnection Connection
		{
			get { return _connection; }
		}

		public GdsDatabase Database
		{
			get { return _database; }
		}

		#endregion

		#region Constructors

		public GdsServiceManager(GdsConnection connection)
		{
			_connection = connection;
			_database = CreateDatabase(_connection);
			RewireWarningMessage();
		}

		#endregion

		#region Methods

		public override async ValueTask AttachAsync(ServiceParameterBufferBase spb, string dataSource, int port, string service, byte[] cryptKey, AsyncWrappingCommonArgs async)
		{
			try
			{
				await SendAttachToBufferAsync(spb, service, async).ConfigureAwait(false);
				await _database.Xdr.FlushAsync(async).ConfigureAwait(false);
				await ProcessAttachResponseAsync((GenericResponse)await _database.ReadResponseAsync(async).ConfigureAwait(false), async).ConfigureAwait(false);
			}
			catch (IOException ex)
			{
				await _database.DetachAsync(async).ConfigureAwait(false);
				throw IscException.ForIOException(ex);
			}
		}

		protected virtual async ValueTask SendAttachToBufferAsync(ServiceParameterBufferBase spb, string service, AsyncWrappingCommonArgs async)
		{
			await _database.Xdr.WriteAsync(IscCodes.op_service_attach, async).ConfigureAwait(false);
			await _database.Xdr.WriteAsync(0, async).ConfigureAwait(false);
			await _database.Xdr.WriteAsync(service, async).ConfigureAwait(false);
			await _database.Xdr.WriteBufferAsync(spb.ToArray(), async).ConfigureAwait(false);
		}

		protected virtual ValueTask ProcessAttachResponseAsync(GenericResponse response, AsyncWrappingCommonArgs async)
		{
			Handle = response.ObjectHandle;
			return ValueTask2.CompletedTask;
		}

		public override async ValueTask DetachAsync(AsyncWrappingCommonArgs async)
		{
			try
			{
				await _database.Xdr.WriteAsync(IscCodes.op_service_detach, async).ConfigureAwait(false);
				await _database.Xdr.WriteAsync(Handle, async).ConfigureAwait(false);
				await _database.Xdr.WriteAsync(IscCodes.op_disconnect, async).ConfigureAwait(false);
				await _database.Xdr.FlushAsync(async).ConfigureAwait(false);

				Handle = 0;
			}
			catch (IOException ex)
			{
				throw IscException.ForIOException(ex);
			}
			finally
			{
				try
				{
					await _connection.DisconnectAsync(async).ConfigureAwait(false);
				}
				catch (IOException ex)
				{
					throw IscException.ForIOException(ex);
				}
				finally
				{
					_database = null;
					_connection = null;
				}
			}
		}

		public override async ValueTask StartAsync(ServiceParameterBufferBase spb, AsyncWrappingCommonArgs async)
		{
			try
			{
				await _database.Xdr.WriteAsync(IscCodes.op_service_start, async).ConfigureAwait(false);
				await _database.Xdr.WriteAsync(Handle, async).ConfigureAwait(false);
				await _database.Xdr.WriteAsync(0, async).ConfigureAwait(false);
				await _database.Xdr.WriteBufferAsync(spb.ToArray(), spb.Length, async).ConfigureAwait(false);
				await _database.Xdr.FlushAsync(async).ConfigureAwait(false);

				try
				{
					await _database.ReadResponseAsync(async).ConfigureAwait(false);
				}
				catch (IscException)
				{
					throw;
				}
			}
			catch (IOException ex)
			{
				throw IscException.ForIOException(ex);
			}
		}

		public override async ValueTask QueryAsync(ServiceParameterBufferBase spb, int requestLength, byte[] requestBuffer, int bufferLength, byte[] buffer, AsyncWrappingCommonArgs async)
		{
			try
			{
				await _database.Xdr.WriteAsync(IscCodes.op_service_info, async).ConfigureAwait(false);
				await _database.Xdr.WriteAsync(Handle, async).ConfigureAwait(false);
				await _database.Xdr.WriteAsync(GdsDatabase.Incarnation, async).ConfigureAwait(false);
				await _database.Xdr.WriteBufferAsync(spb.ToArray(), spb.Length, async).ConfigureAwait(false);
				await _database.Xdr.WriteBufferAsync(requestBuffer, requestLength, async).ConfigureAwait(false);
				await _database.Xdr.WriteAsync(bufferLength, async).ConfigureAwait(false);

				await _database.Xdr.FlushAsync(async).ConfigureAwait(false);

				var response = (GenericResponse)await _database.ReadResponseAsync(async).ConfigureAwait(false);

				var responseLength = bufferLength;

				if (response.Data.Length < bufferLength)
				{
					responseLength = response.Data.Length;
				}

				Buffer.BlockCopy(response.Data, 0, buffer, 0, responseLength);
			}
			catch (IOException ex)
			{
				throw IscException.ForIOException(ex);
			}
		}

		public override ServiceParameterBufferBase CreateServiceParameterBuffer()
		{
			return new ServiceParameterBuffer2();
		}

		protected virtual GdsDatabase CreateDatabase(GdsConnection connection)
		{
			return new GdsDatabase(connection);
		}

		private void RewireWarningMessage()
		{
			_database.WarningMessage = ex => WarningMessage?.Invoke(ex);
		}

		#endregion
	}
}
