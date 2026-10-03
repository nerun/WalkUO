/* ***************************************************************************
 * NativeReader.cs
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
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace Server {
    public static class NativeReader {

        private static readonly INativeReader m_NativeReader;

        static NativeReader() {
            if ( Core.Unix )
                m_NativeReader = new NativeReaderUnix();
            else
                m_NativeReader = new NativeReaderWin32();
        }

        public static unsafe void Read( IntPtr ptr, void *buffer, int length ) {
            m_NativeReader.Read( ptr, buffer, length );
        }
    }

    public interface INativeReader {
        unsafe void Read( IntPtr ptr, void *buffer, int length );
    }

    public sealed class NativeReaderWin32 : INativeReader {
        internal class UnsafeNativeMethods {
            /*[DllImport("kernel32")]
            internal unsafe static extern int _lread(IntPtr hFile, void* lpBuffer, int wBytes);*/

            [DllImport("kernel32", SetLastError = true)]
            internal unsafe static extern bool ReadFile(IntPtr hFile, void* lpBuffer, uint nNumberOfBytesToRead, ref uint lpNumberOfBytesRead, NativeOverlapped* lpOverlapped);
        }

        public NativeReaderWin32() {
        }

        public unsafe void Read( IntPtr ptr, void *buffer, int length ) {
            if ( length < 0 )
                throw new ArgumentOutOfRangeException( "length" );

            byte* current = (byte*)buffer;

            while ( length > 0 ) {
                uint bytesRead = 0;

                if ( !UnsafeNativeMethods.ReadFile( ptr, current, (uint)length, ref bytesRead, null ) )
                    throw new IOException( "Native file read failed.", new Win32Exception( Marshal.GetLastWin32Error() ) );

                if ( bytesRead == 0 )
                    throw new EndOfStreamException();

                current += (int)bytesRead;
                length -= (int)bytesRead;
            }
        }
    }

    public sealed class NativeReaderUnix : INativeReader {
        internal class UnsafeNativeMethods {
            [DllImport("libc", SetLastError = true)]
            internal unsafe static extern IntPtr read(IntPtr ptr, void* buffer, UIntPtr length);
        }

        public NativeReaderUnix() {
        }

        public unsafe void Read( IntPtr ptr, void *buffer, int length ) {
            if ( length < 0 )
                throw new ArgumentOutOfRangeException( "length" );

            byte* current = (byte*)buffer;

            while ( length > 0 ) {
                long bytesRead = UnsafeNativeMethods.read( ptr, current, new UIntPtr( (uint)length ) ).ToInt64();

                if ( bytesRead < 0 ) {
                    int error = Marshal.GetLastWin32Error();

                    if ( error == 4 ) // EINTR
                        continue;

                    throw new IOException( "Native file read failed (errno " + error + ")." );
                }

                if ( bytesRead == 0 )
                    throw new EndOfStreamException();

                current += (int)bytesRead;
                length -= (int)bytesRead;
            }
        }
    }
}
