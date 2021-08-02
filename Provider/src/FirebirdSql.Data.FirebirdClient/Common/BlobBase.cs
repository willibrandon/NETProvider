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

namespace FirebirdSql.Data.Common
{
	internal abstract class BlobBase
	{
		private int _rblFlags;
		private Charset _charset;
		private int _segmentSize;

		protected long _blobId;
		protected int _position;
		protected TransactionBase _transaction;

		public abstract int Handle { get; }
		public long Id => _blobId;
		public bool EOF => (_rblFlags & IscCodes.RBL_eof_pending) != 0;

		protected int SegmentSize => _segmentSize;

		public abstract DatabaseBase Database { get; }

		protected BlobBase(DatabaseBase db)
		{
			_segmentSize = db.PacketSize;
			_charset = db.Charset;
		}

		public async ValueTask<string> ReadStringAsync(AsyncWrappingCommonArgs async)
		{
			var buffer = await ReadAsync(async).ConfigureAwait(false);
			return _charset.GetString(buffer, 0, buffer.Length);
		}

		public async ValueTask<byte[]> ReadAsync(AsyncWrappingCommonArgs async)
		{
			using (var ms = new MemoryStream())
			{
				try
				{
					await OpenAsync(async).ConfigureAwait(false);

					while (!EOF)
					{
						await GetSegmentAsync(ms, async).ConfigureAwait(false);
					}

					await CloseAsync(async).ConfigureAwait(false);
				}
				catch
				{
					// Cancel the blob and rethrow the exception
					await CancelAsync(async).ConfigureAwait(false);

					throw;
				}

				return ms.ToArray();
			}
		}

		public ValueTask WriteAsync(string data, AsyncWrappingCommonArgs async)
		{
			return WriteAsync(_charset.GetBytes(data), async);
		}

		public ValueTask WriteAsync(byte[] buffer, AsyncWrappingCommonArgs async)
		{
			return WriteAsync(buffer, 0, buffer.Length, async);
		}

		public async ValueTask WriteAsync(byte[] buffer, int index, int count, AsyncWrappingCommonArgs async)
		{
			try
			{
				await CreateAsync(async).ConfigureAwait(false);

				var length = count;
				var offset = index;
				var chunk = length >= _segmentSize ? _segmentSize : length;

				var tmpBuffer = new byte[chunk];

				while (length > 0)
				{
					if (chunk > length)
					{
						chunk = length;
						tmpBuffer = new byte[chunk];
					}

					Array.Copy(buffer, offset, tmpBuffer, 0, chunk);
					await PutSegmentAsync(tmpBuffer, async).ConfigureAwait(false);

					offset += chunk;
					length -= chunk;
				}

				await CloseAsync(async).ConfigureAwait(false);
			}
			catch
			{
				// Cancel the blob and rethrow the exception
				await CancelAsync(async).ConfigureAwait(false);

				throw;
			}
		}

		protected abstract ValueTask CreateAsync(AsyncWrappingCommonArgs async);
		protected abstract ValueTask OpenAsync(AsyncWrappingCommonArgs async);
		protected abstract ValueTask GetSegmentAsync(Stream stream, AsyncWrappingCommonArgs async);
		protected abstract ValueTask PutSegmentAsync(byte[] buffer, AsyncWrappingCommonArgs async);
		protected abstract ValueTask SeekAsync(int position, AsyncWrappingCommonArgs async);
		protected abstract ValueTask CloseAsync(AsyncWrappingCommonArgs async);
		protected abstract ValueTask CancelAsync(AsyncWrappingCommonArgs async);

		protected void RblAddValue(int rblValue)
		{
			_rblFlags |= rblValue;
		}

		protected void RblRemoveValue(int rblValue)
		{
			_rblFlags &= ~rblValue;
		}
	}
}
