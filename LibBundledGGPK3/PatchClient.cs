﻿using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Threading.Tasks;

using SystemExtensions;
using SystemExtensions.Collections;
using SystemExtensions.Spans;

namespace LibBundledGGPK3;

/// <summary>
/// Client to interact with the patch server.
/// </summary>
/// <remarks>
/// <para>Currently supports protocol version 6 only.</para>
/// <para>
/// Call <see cref="ConnectAsync(EndPoint)"/> before using other methods.<br />
/// Sample server endpoints are in <see cref="ServerEndPoints"/>.
/// </para>
/// </remarks>
public class PatchClient : IDisposable {
	public static class ServerEndPoints {
		/// <summary>
		/// patch.pathofexile.com:12995
		/// </summary>
		public static readonly DnsEndPoint US = new("patch.pathofexile.com", 12995);
		/// <summary>
		/// patch.pathofexile.tw:12999
		/// </summary>
		public static readonly DnsEndPoint TW = new("patch.pathofexile.tw", 12999);
	}

	protected readonly Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
	protected Task? lastRequest;

	public virtual byte ProtocolVersion => 6;
	public virtual bool Connected => CdnUrl is not null && socket.Connected;
	/// <summary>
	/// CDN URL to download patch files. Only available after <see cref="ConnectAsync(EndPoint)"/> is called and completed.
	/// </summary>
	public string? CdnUrl { get; protected set; }

	public PatchClient() {
		socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
		socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 10000); // 10 seconds
	}

	public virtual Task ConnectAsync(EndPoint server) {
		lock (socket) {
			if (lastRequest is not null && !lastRequest.IsCompleted)
				ThrowHelper.Throw<InvalidOperationException>("Another task is running. ConnectAsync must be the first method to be called.");
			return lastRequest = Core();
		}

		async Task Core() {
			await socket.ConnectAsync(server).ConfigureAwait(false);

			var array = ArrayPool<byte>.Shared.Rent(256);
			try {
				array[0] = 1; // Opcode
				array[1] = ProtocolVersion;
				await socket.SendAsync(new ArraySegment<byte>(array, 0, 2));

				var len = await socket.ReceiveAsync(array);
				ParsePacket(new(array, 0, len));
			} finally {
				ArrayPool<byte>.Shared.Return(array);
			}

			void ParsePacket(ReadOnlySpan<byte> span) {
				if (span.ReadAndSlice<byte>() != 2) // Opcode
					ThrowInvalidOpcode();
				span = span.Slice(32); // 32 bytes empty
				var length = span.ReadAndSlice<ushort>();
				Utils.EnsureBigEndian(ref length);
				CdnUrl = MemoryMarshal.Cast<byte, char>(span)[..length].ToString();
			}
		}
	}

	public virtual Task<EntryInfo[]> QueryDirectoryAsync(string directoryName) {
		Task? toWait = null;
		lock (socket) {
			if (lastRequest is not null && !lastRequest.IsCompleted)
				toWait = lastRequest;
			var result = Core();
			lastRequest = result;
			return result;
		}

		async Task<EntryInfo[]> Core(Task? _ = null) {
			if (toWait is not null)
				await toWait;

			const int ResponseHeaderLength = sizeof(byte) + sizeof(uint) + sizeof(uint) + sizeof(uint); // Opcode + compressedLen + decompressedLen + compressedLen
			var len = sizeof(byte) + sizeof(ushort) + directoryName.Length * sizeof(char);
			var a = new ArrayPoolRenter<byte>(Math.Max(ResponseHeaderLength, len));
			var array = a.Array;

			FillPacket(array);
			await socket.SendAsync(new ArraySegment<byte>(array, 0, len));

			len = await socket.ReceiveAsync(new ArraySegment<byte>(array, 0, ResponseHeaderLength));
			int compressedLen;
			int decompressedLen;
			GetLength(new(array, 0, len));

			if (array.Length < compressedLen)
				a.Resize(compressedLen);
			len = 0;
			do {
				len += await socket.ReceiveAsync(new ArraySegment<byte>(array, len, compressedLen - len));
			} while (len < compressedLen);
			return ParsePacket(new(array, 0, len));

			void FillPacket(Span<byte> span) {
				span.WriteAndSlice<byte>(3); // Opcode
				BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)directoryName.Length);
				MemoryMarshal.Cast<char, byte>(directoryName.AsSpan()).CopyTo(span.Slice(sizeof(ushort)));
			}

			void GetLength(ReadOnlySpan<byte> span) {
				if (span.ReadAndSlice<byte>() != 4) // Opcode
					ThrowInvalidOpcode();
				compressedLen = span.ReadAndSlice<int>();
				Utils.EnsureBigEndian(ref compressedLen);
				decompressedLen = MemoryMarshal.Read<int>(span);
				Utils.EnsureBigEndian(ref compressedLen);
				// Skip second compressedLen
			}

			EntryInfo[] ParsePacket(ReadOnlySpan<byte> span) {
				var array = ArrayPool<byte>.Shared.Rent(decompressedLen);
				try {
					decompressedLen = LZ4.Decompress(span, array);
					var resultSpan = new ReadOnlySpan<byte>(array, 0, decompressedLen);
					a.Dispose();

					resultSpan = resultSpan.Slice(sizeof(int) + BinaryPrimitives.ReadInt32LittleEndian(resultSpan) * sizeof(char), decompressedLen); // Skip directoryName
					var count = resultSpan.ReadAndSlice<int>();
					Utils.EnsureLittleEndian(ref count);
					var result = new EntryInfo[count];
					for (var i = 0; i < count; i++) {
						var type = resultSpan.ReadAndSlice<byte>();
						var fileSize = resultSpan.ReadAndSlice<int>();
						if (type == 1)
							fileSize = -1;

						var nameLength = resultSpan.ReadAndSlice<int>();
						Utils.EnsureLittleEndian(ref nameLength);
						nameLength *= sizeof(char);
						var name = MemoryMarshal.Cast<byte, char>(resultSpan[..nameLength]).ToString();
						resultSpan = resultSpan.SliceUnchecked(nameLength);

						result[i] = new(fileSize, name, /*hash*/resultSpan.ReadAndSlice<Vector256<byte>>());
					}
					return result;
				} finally {
					ArrayPool<byte>.Shared.Return(array);
				}
			}
		}
	}

	public virtual Task<string> QueryPatchNotesUrlAsync() {
		Task? toWait = null;
		lock (socket) {
			if (lastRequest is not null && !lastRequest.IsCompleted)
				toWait = lastRequest;
			var result = Core();
			lastRequest = result;
			return result;
		}

		async Task<string> Core(Task? _ = null) {
			if (toWait is not null)
				await toWait;
			var array = ArrayPool<byte>.Shared.Rent(256);
			try {
				array[0] = 5; // Opcode
				await socket.SendAsync(new ArraySegment<byte>(array, 0, 1));

				var len = await socket.ReceiveAsync(array);
				return ParsePacket(new(array, 0, len));

				string ParsePacket(ReadOnlySpan<byte> span) {
					if (span.ReadAndSlice<byte>() != 6) // Opcode
						ThrowInvalidOpcode();
					var length = span.ReadAndSlice<ushort>();
					Utils.EnsureBigEndian(ref length);
					return MemoryMarshal.Cast<byte, char>(span)[..length].ToString();
				}
			} finally {
				ArrayPool<byte>.Shared.Return(array);
			}
		}
	}

	public readonly struct EntryInfo(int fileSize, string name, Vector256<byte> hash) {
		public readonly int FileSize = fileSize;
		public readonly string Name = name;
		public readonly Vector256<byte> Hash = hash;
	}

	/// <exception cref="InvalidDataException"/>
	[DoesNotReturn]
	[DebuggerNonUserCode]
	protected static void ThrowInvalidOpcode() {
		throw new InvalidDataException("Invalid response opcode");
	}

	public virtual void Dispose() {
		GC.SuppressFinalize(this);
		socket.Dispose();
	}
}