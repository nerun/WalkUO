/* ***************************************************************************
 * FileOperations.cs
 *
 * RunUO is an open-source server emulator for Ultima Online.
 * Copyright (C) 2002  The RunUO Software Team
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License along
 * with this program; if not, see <https://www.gnu.org/licenses/>.
 ***************************************************************************/
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

#if !MONO
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
#endif

namespace Server {
    public static class FileOperations {
        public const int KB = 1024;
        public const int MB = 1024 * KB;

#if !MONO
        private const FileOptions NoBuffering = ( FileOptions ) 0x20000000;

        internal static class UnsafeNativeMethods {
            [DllImport("Kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern SafeFileHandle CreateFile(string lpFileName, int dwDesiredAccess, FileShare dwShareMode, IntPtr securityAttrs, FileMode dwCreationDisposition, int dwFlagsAndAttributes, IntPtr hTemplateFile);

            [StructLayout(LayoutKind.Sequential)]
            internal struct StorageInfo {
                internal uint LogicalBytesPerSector;
                internal uint PhysicalBytesPerSectorForAtomicity;
                internal uint PhysicalBytesPerSectorForPerformance;
                internal uint FileSystemEffectivePhysicalBytesPerSectorForAtomicity;
                internal uint Flags;
                internal uint ByteOffsetForSectorAlignment;
                internal uint ByteOffsetForPartitionAlignment;
            }

            [DllImport("Kernel32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out StorageInfo info, uint size);

            [DllImport("Kernel32", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetAlignmentInfo(SafeFileHandle handle, int infoClass, out uint alignment, uint size);

            [DllImport("Kernel32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool ReadFile(SafeFileHandle handle, IntPtr buffer, int count, out int read, IntPtr overlapped);

            [DllImport("Kernel32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, ref long length, uint size);
        }
#endif

        private static int bufferSize = 1 * MB;
        private static int concurrency = 1;

        private static bool unbuffered = true;

        public static int BufferSize {
            get {
                return bufferSize;
            }
            set {
                bufferSize = value;
            }
        }

        public static int Concurrency {
            get {
                return concurrency;
            }
            set {
                concurrency = value;
            }
        }

        public static bool Unbuffered {
            get {
                return unbuffered;
            }
            set {
                unbuffered = value;
            }
        }

        public static bool AreSynchronous {
            get {
                return ( concurrency < 1 );
            }
        }

        public static bool AreAsynchronous {
            get {
                return ( concurrency > 0 );
            }
        }

        public static FileStream OpenSequentialStream( string path, FileMode mode, FileAccess access, FileShare share ) {
            FileOptions options = FileOptions.SequentialScan;

            if ( concurrency > 0 ) {
                options |= FileOptions.Asynchronous;
            }

#if MONO
            return new FileStream( path, mode, access, share, bufferSize, options );
#else
            if ( unbuffered ) {
                options |= NoBuffering;
            } else {
                return new FileStream( path, mode, access, share, bufferSize, options );
            }

            if (path == null)
                throw new ArgumentNullException("path");
            if (bufferSize < 1)
                throw new ArgumentOutOfRangeException("BufferSize");
            if (access < FileAccess.Read || access > FileAccess.ReadWrite)
                throw new ArgumentOutOfRangeException("access");
            if (mode < FileMode.CreateNew || mode > FileMode.Append)
                throw new ArgumentOutOfRangeException("mode");
            if ((mode == FileMode.Create || mode == FileMode.CreateNew || mode == FileMode.Truncate || mode == FileMode.Append) && (access & FileAccess.Write) == 0)
                throw new ArgumentException("This file mode requires write access.");
            if (mode == FileMode.Append && access != FileAccess.Write)
                throw new ArgumentException("Append requires write-only access.");

            // Partial sectors need read/modify/write, even for a write-only caller.
            // Native transfers are serialized; BeginWrite queues work off the caller's thread.
            FileAccess nativeAccess = access == FileAccess.Read ? FileAccess.Read : FileAccess.ReadWrite;
            int desiredAccess = nativeAccess == FileAccess.Read ? unchecked((int)0x80000000) : unchecked((int)0xC0000000);
            options &= ~FileOptions.Asynchronous;
            // Query alignment before truncating an existing save.
            FileMode nativeMode = mode == FileMode.Append || mode == FileMode.Create ? FileMode.OpenOrCreate :
                mode == FileMode.Truncate ? FileMode.Open : mode;
            SafeFileHandle fileHandle = UnsafeNativeMethods.CreateFile(path, desiredAccess, share, IntPtr.Zero, nativeMode, (int)options, IntPtr.Zero);

            try {
                if (fileHandle.IsInvalid)
                    throw NativeError("Opening an unbuffered file failed.");

                UnbufferedFileStream stream = new UnbufferedFileStream(fileHandle, access, bufferSize, mode == FileMode.Append);
                if (mode == FileMode.Create || mode == FileMode.Truncate)
                    stream.SetLength(0);
                return stream;
            } catch {
                fileHandle.Dispose();
                throw;
            }
#endif
        }

#if !MONO
        private static IOException NativeError(string message) {
            return new IOException(message, new Win32Exception(Marshal.GetLastWin32Error()));
        }

        private sealed class UnbufferedFileStream : FileStream {
            private readonly object sync = new object();
            private readonly SafeFileHandle handle;
            private readonly FileAccess access;
            private readonly int sectorSize, alignment, capacity;
            private readonly byte[] transfer;
            private readonly long appendStart;
            // Physical EOF may include zero padding, as in legacy saves. World
            // indices and these positions count only the caller's logical bytes.
            private long position, length;
            private bool closed;
            private Task pending = Task.FromResult(0);
            private ExceptionDispatchInfo writeFailure;

            public UnbufferedFileStream(SafeFileHandle handle, FileAccess access, int bufferSize, bool append)
                : base(handle, access == FileAccess.Read ? FileAccess.Read : FileAccess.ReadWrite, 1, false) {
                this.handle = handle;
                this.access = access;

                UnsafeNativeMethods.StorageInfo storage;
                uint mask;
                // FILE_STORAGE_INFO (16) and FILE_ALIGNMENT_INFO (17).
                if (!UnsafeNativeMethods.GetFileInformationByHandleEx(handle, 16, out storage, 28) ||
                    !UnsafeNativeMethods.GetAlignmentInfo(handle, 17, out mask, 4))
                    throw NativeError("Querying unbuffered file alignment failed.");

                CheckedAlignment(storage.LogicalBytesPerSector);
                sectorSize = CheckedAlignment(Math.Max(storage.LogicalBytesPerSector,
                    Math.Max(storage.PhysicalBytesPerSectorForAtomicity, storage.FileSystemEffectivePhysicalBytesPerSectorForAtomicity)));
                alignment = Math.Max(sectorSize, CheckedAlignment((ulong)mask + 1));
                capacity = checked((int)(((long)bufferSize + sectorSize - 1) / sectorSize * sectorSize));
                transfer = new byte[checked(capacity + alignment - 1)];
                length = base.Length;
                position = append ? length : 0;
                appendStart = append ? length : 0;
            }

            private static int CheckedAlignment(ulong value) {
                if (value == 0 || value > Int32.MaxValue || (value & (value - 1)) != 0)
                    throw new IOException("The file reported an unsupported I/O alignment.");
                return (int)value;
            }

            private void CheckOpen() {
                if (closed || handle.IsClosed)
                    throw new ObjectDisposedException("UnbufferedFileStream");
            }

            private static void CheckBuffer(byte[] buffer, int offset, int count) {
                if (buffer == null)
                    throw new ArgumentNullException("buffer");
                if (offset < 0 || count < 0)
                    throw new ArgumentOutOfRangeException(offset < 0 ? "offset" : "count");
                if (buffer.Length - offset < count)
                    throw new ArgumentException("The buffer range is invalid.");
            }

            // Serialize physical writes, including a partial sector rewritten after Flush.
            // Workers never take sync: callers may wait for them while holding that lock.
            private T Run<T>(Func<T> action) {
                lock (sync) {
                    CheckOpen();
                    WaitForPending();
                    return action();
                }
            }

            private void WaitForPending() {
                // APM/TAP callers observe their own result; cancellation/read errors
                // do not invalidate later operations. A failed write does.
                try { pending.GetAwaiter().GetResult(); }
                catch { }
                if (writeFailure != null)
                    writeFailure.Throw();
            }

            private Task<int> Queue(Func<int> action, CancellationToken cancellationToken = default(CancellationToken)) {
                lock (sync) {
                    CheckOpen();
                    TaskCompletionSource<int> result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                    // Keep the ordering barrier separate from the caller's result:
                    // a canceled request must not let later writes overtake its predecessor.
                    pending = pending.ContinueWith(previous => {
                        try {
                            if (writeFailure != null)
                                writeFailure.Throw();
                            cancellationToken.ThrowIfCancellationRequested();
                            result.SetResult(action());
                        } catch (OperationCanceledException error) {
                            if (cancellationToken.IsCancellationRequested)
                                result.SetCanceled();
                            else
                                result.SetException(error);
                        } catch (Exception error) {
                            result.SetException(error);
                        }
                    }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                    return result.Task;
                }
            }

            private int AlignedOffset(GCHandle pin) {
                ulong address = unchecked((ulong)pin.AddrOfPinnedObject().ToInt64());
                return (int)((0UL - address) & (uint)(alignment - 1));
            }

            private void ReadSectorRange(long start, int offset, int size) {
                Array.Clear(transfer, offset, size);
                if (start >= length)
                    return;

                base.Seek(start, SeekOrigin.Begin);
                int read;
                // The caller pins transfer for the entire native operation.
                GCHandle pin = GCHandle.Alloc(transfer, GCHandleType.Pinned);
                try {
                    if (!UnsafeNativeMethods.ReadFile(handle, IntPtr.Add(pin.AddrOfPinnedObject(), offset), size, out read, IntPtr.Zero))
                        throw NativeError("Reading an unbuffered sector failed.");
                } finally {
                    pin.Free();
                }

                int valid = (int)Math.Min(size, length - start);
                if (read < valid)
                    throw new EndOfStreamException("An unbuffered sector was only partially read.");
                // Do not expose or reuse bytes beyond the logical end of the stream.
                Array.Clear(transfer, offset + valid, size - valid);
            }

            private void WriteAligned(byte[] array, int offset, int count) {
                if ((access & FileAccess.Write) == 0)
                    throw new NotSupportedException("The stream is not writable.");
                if (count > Int64.MaxValue - position)
                    throw new IOException("The write exceeds the supported file size.");
                if (count == 0)
                    return;

                GCHandle pin = GCHandle.Alloc(transfer, GCHandleType.Pinned);
                try {
                    int aligned = AlignedOffset(pin);
                    while (count > 0) {
                        int prefix = (int)(position % sectorSize);
                        long start = position - prefix;
                        int take = Math.Min(count, capacity - prefix);
                        int physical = (int)(((long)prefix + take + sectorSize - 1) / sectorSize * sectorSize);
                        if (start > Int64.MaxValue - physical)
                            throw new IOException("The aligned write exceeds the supported file size.");

                        if (prefix != 0 || physical != take)
                            ReadSectorRange(start, aligned, physical);
                        Buffer.BlockCopy(array, offset, transfer, aligned + prefix, take);
                        base.Seek(start, SeekOrigin.Begin);
                        // Buffer size 1 in the base stream prevents managed re-buffering.
                        base.Write(transfer, aligned, physical);
                        position += take;
                        length = Math.Max(length, position);
                        offset += take;
                        count -= take;
                    }
                } catch (Exception error) {
                    writeFailure = ExceptionDispatchInfo.Capture(error);
                    throw;
                } finally {
                    pin.Free();
                }
            }

            private int ReadAligned(byte[] array, int offset, int count) {
                if ((access & FileAccess.Read) == 0)
                    throw new NotSupportedException("The stream is not readable.");
                int total = 0;
                GCHandle pin = GCHandle.Alloc(transfer, GCHandleType.Pinned);
                try {
                    int aligned = AlignedOffset(pin);
                    while (count > 0 && position < length) {
                        int prefix = (int)(position % sectorSize);
                        int take = (int)Math.Min(Math.Min(count, capacity - prefix), length - position);
                        int physical = (int)(((long)prefix + take + sectorSize - 1) / sectorSize * sectorSize);
                        ReadSectorRange(position - prefix, aligned, physical);
                        Buffer.BlockCopy(transfer, aligned + prefix, array, offset, take);
                        position += take;
                        offset += take;
                        count -= take;
                        total += take;
                    }
                } finally {
                    pin.Free();
                }
                return total;
            }

            public override bool CanRead { get { return !closed && (access & FileAccess.Read) != 0 && base.CanRead; } }
            public override bool CanWrite { get { return !closed && (access & FileAccess.Write) != 0 && base.CanWrite; } }
            public override long Length { get { return Run(() => length); } }
            public override long Position {
                get { return Run(() => position); }
                set { Seek(value, SeekOrigin.Begin); }
            }

            public override long Seek(long offset, SeekOrigin origin) {
                if (origin < SeekOrigin.Begin || origin > SeekOrigin.End)
                    throw new ArgumentOutOfRangeException("origin");
                return Run(() => {
                    long next = checked((origin == SeekOrigin.Begin ? 0 : origin == SeekOrigin.Current ? position : length) + offset);
                    if (next < appendStart)
                        throw new IOException("Cannot seek before the beginning of the stream or append region.");
                    position = next;
                    return position;
                });
            }

            public override void SetLength(long value) {
                if (value < 0)
                    throw new ArgumentOutOfRangeException("value");
                Run(() => {
                    if (!CanWrite)
                        throw new NotSupportedException("The stream is not writable.");
                    if (value < appendStart)
                        throw new IOException("Cannot truncate the append region.");
                    // FILE_END_OF_FILE_INFO (6): metadata, not an unaligned data transfer.
                    if (!UnsafeNativeMethods.SetFileInformationByHandle(handle, 6, ref value, 8))
                        throw NativeError("Changing the unbuffered file length failed.");
                    length = value;
                    position = Math.Min(position, value);
                    return 0;
                });
            }

            public override void Write(byte[] array, int offset, int count) {
                CheckBuffer(array, offset, count);
                Run(() => { WriteAligned(array, offset, count); return 0; });
            }

            public override int Read(byte[] array, int offset, int count) {
                CheckBuffer(array, offset, count);
                return Run(() => ReadAligned(array, offset, count));
            }

            public override void WriteByte(byte value) { Write(new byte[] { value }, 0, 1); }
            public override int ReadByte() {
                byte[] value = new byte[1];
                return Read(value, 0, 1) == 0 ? -1 : value[0];
            }

            private sealed class AsyncResult : IAsyncResult {
                internal UnbufferedFileStream Owner;
                internal Task<int> Task;
                internal bool Write;
                internal int Ended;
                internal object State;
                public object AsyncState { get { return State; } }
                public WaitHandle AsyncWaitHandle { get { return ((IAsyncResult)Task).AsyncWaitHandle; } }
                public bool CompletedSynchronously { get { return false; } }
                public bool IsCompleted { get { return Task.IsCompleted; } }
            }

            private IAsyncResult BeginOperation(byte[] array, int offset, int count, AsyncCallback callback, object state, bool write) {
                CheckBuffer(array, offset, count);
                if (write ? !CanWrite : !CanRead) {
                    CheckOpen();
                    throw new NotSupportedException("The stream does not support this operation.");
                }
                AsyncResult result = new AsyncResult { Owner = this, State = state, Write = write };
                result.Task = Queue(() => {
                    if (!write)
                        return ReadAligned(array, offset, count);
                    WriteAligned(array, offset, count);
                    return 0;
                });
                if (callback != null) {
                    // Do not capture a caller's synchronization context or swallow
                    // callback exceptions in an unobserved continuation Task.
                    result.Task.ConfigureAwait(false).GetAwaiter().OnCompleted(() => callback(result));
                }
                return result;
            }

            private int EndOperation(IAsyncResult result, bool write) {
                if (result == null)
                    throw new ArgumentNullException("result");
                AsyncResult operation = result as AsyncResult;
                if (operation == null || operation.Owner != this || operation.Write != write)
                    throw new ArgumentException("The asynchronous result belongs to another operation.");
                if (Interlocked.Exchange(ref operation.Ended, 1) != 0)
                    throw new InvalidOperationException("The asynchronous operation was already ended.");
                return operation.Task.GetAwaiter().GetResult();
            }

            public override IAsyncResult BeginWrite(byte[] array, int offset, int count, AsyncCallback callback, object state) {
                return BeginOperation(array, offset, count, callback, state, true);
            }
            public override void EndWrite(IAsyncResult result) { EndOperation(result, true); }
            public override IAsyncResult BeginRead(byte[] array, int offset, int count, AsyncCallback callback, object state) {
                return BeginOperation(array, offset, count, callback, state, false);
            }
            public override int EndRead(IAsyncResult result) { return EndOperation(result, false); }

            public override Task WriteAsync(byte[] array, int offset, int count, CancellationToken cancellationToken) {
                CheckBuffer(array, offset, count);
                return Queue(() => { WriteAligned(array, offset, count); return 0; }, cancellationToken);
            }
            public override Task<int> ReadAsync(byte[] array, int offset, int count, CancellationToken cancellationToken) {
                CheckBuffer(array, offset, count);
                return Queue(() => ReadAligned(array, offset, count), cancellationToken);
            }

            public override void Flush() { Flush(false); }
            public override void Flush(bool flushToDisk) {
                Run(() => { base.Flush(flushToDisk); return 0; });
            }
            public override Task FlushAsync(CancellationToken cancellationToken) {
                return Queue(() => { base.Flush(false); return 0; }, cancellationToken);
            }

            protected override void Dispose(bool disposing) {
                if (!disposing || sync == null) {
                    base.Dispose(disposing);
                    return;
                }
                lock (sync) {
                    if (closed)
                        return;
                    try { WaitForPending(); }
                    finally {
                        closed = true;
                        base.Dispose(true);
                    }
                }
            }
        }
#endif
    }
}
