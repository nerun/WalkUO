/* ***************************************************************************
 * SequentialFileWriter.cs
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
using System.Threading;
using System.Runtime.ExceptionServices;

namespace Server {
    public sealed class SequentialFileWriter : Stream {
        private FileStream fileStream;
        private FileQueue fileQueue;

        private AsyncCallback writeCallback;

        private SaveMetrics metrics;

        public SequentialFileWriter( string path, SaveMetrics metrics ) {
            if ( path == null ) {
                throw new ArgumentNullException( "path" );
            }

            this.metrics = metrics;

            this.fileStream = FileOperations.OpenSequentialStream( path, FileMode.Create, FileAccess.Write, FileShare.None );

            try {
                fileQueue = new FileQueue(
#if MONO
                    // Mono's asynchronous writes share the stream position and buffer.
                    1,
#else
                    Math.Max( 1, FileOperations.Concurrency ),
#endif
                    FileCallback
                );
            } catch {
                fileStream.Close();
                throw;
            }
        }

        private void CheckDisposed() {
            if ( fileStream == null )
                throw new ObjectDisposedException( "SequentialFileWriter" );
        }

        public override long Position {
            get {
                CheckDisposed();
                return fileQueue.Position;
            }
            set {
                throw new InvalidOperationException();
            }
        }

        private void FileCallback( FileQueue.Chunk chunk ) {
            if ( FileOperations.AreSynchronous ) {
                fileStream.Write( chunk.Buffer, chunk.Offset, chunk.Size );

                if ( metrics != null ) {
                    metrics.OnFileWritten( chunk.Size );
                }

                chunk.Commit();
            } else {
                if ( writeCallback == null ) {
                    writeCallback = this.OnWrite;
                }

                fileStream.BeginWrite( chunk.Buffer, chunk.Offset, chunk.Size, writeCallback, chunk );
            }
        }

        private void OnWrite( IAsyncResult asyncResult ) {
            FileQueue.Chunk chunk = asyncResult.AsyncState as FileQueue.Chunk;

            try {
                fileStream.EndWrite( asyncResult );

                if ( metrics != null ) {
                    metrics.OnFileWritten( chunk.Size );
                }
            } catch ( Exception ex ) {
                chunk.Fail( ex );
                return;
            }

            chunk.Commit();
        }

        public override void Write( byte[] buffer, int offset, int size ) {
            CheckDisposed();
            fileQueue.Enqueue( buffer, offset, size );
        }

        public override void Flush() {
            CheckDisposed();
            fileQueue.Flush();
            fileStream.Flush();
        }

        protected override void Dispose( bool disposing ) {
            try {
                if ( fileStream != null ) {
                    ExceptionDispatchInfo error = null;

                    try { Flush(); }
                    catch ( Exception ex ) { error = ExceptionDispatchInfo.Capture( ex ); }

                    try { fileQueue.Dispose(); }
                    catch ( Exception ex ) {
                        if ( error == null )
                            error = ExceptionDispatchInfo.Capture( ex );
                    } finally {
                        fileQueue = null;
                    }

                    try { fileStream.Close(); }
                    catch ( Exception ex ) {
                        if ( error == null )
                            error = ExceptionDispatchInfo.Capture( ex );
                    } finally {
                        fileStream = null;
                    }

                    if ( error != null )
                        error.Throw();
                }
            } finally {
                base.Dispose( disposing );
            }
        }

        public override bool CanRead {
            get { return false; }
        }

        public override bool CanSeek {
            get { return false; }
        }

        public override bool CanWrite {
            get { return fileStream != null; }
        }

        public override long Length {
            get { return this.Position; }
        }

        public override int Read( byte[] buffer, int offset, int count ) {
            throw new InvalidOperationException();
        }

        public override long Seek( long offset, SeekOrigin origin ) {
            throw new InvalidOperationException();
        }

        public override void SetLength( long value ) {
            CheckDisposed();
            throw new NotSupportedException();
        }
    }
}
