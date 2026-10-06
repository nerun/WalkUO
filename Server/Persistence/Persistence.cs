/* ***************************************************************************
 * Persistence.cs
 *
 * RunUO is an open-source server emulator for Ultima Online.
 * Copyright (C)  The RunUO Software Team
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
#region References
using System;
using System.IO;
using System.Runtime.ExceptionServices;
#endregion

namespace Server
{
    public static class Persistence
    {
        public static void Serialize(string path, Action<GenericWriter> serializer)
        {
            Serialize(new FileInfo(path), serializer);
        }

        public static void Serialize(FileInfo file, Action<GenericWriter> serializer)
        {
            file.Refresh();

            if (file.Directory != null && !file.Directory.Exists)
            {
                file.Directory.Create();
            }

            if (!file.Exists)
            {
                file.Create().Close();
            }
                
            file.Refresh();

            using (var fs = file.Open(FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var writer = new BinaryFileWriter(fs, true);
                ExceptionDispatchInfo failure = null;

                try
                {
                    serializer(writer);
                }
                catch (Exception e)
                {
                    failure = ExceptionDispatchInfo.Capture(e);
                }
                finally
                {
                    try
                    {
                        writer.Close();
                    }
                    catch (Exception e)
                    {
                        if (failure == null)
                            failure = ExceptionDispatchInfo.Capture(e);
                    }
                }

                if (failure != null)
                    failure.Throw();
            }
        }

        public static void Deserialize(string path, Action<GenericReader> deserializer)
        {
            Deserialize(path, deserializer, true);
        }

        public static void Deserialize(FileInfo file, Action<GenericReader> deserializer)
        {
            Deserialize(file, deserializer, true);
        } 

        public static void Deserialize(string path, Action<GenericReader> deserializer, bool ensure)
        {
            Deserialize(new FileInfo(path), deserializer, ensure);
        }

        public static void Deserialize(FileInfo file, Action<GenericReader> deserializer, bool ensure)
        {
            file.Refresh();

            if (file.Directory != null && !file.Directory.Exists)
            {
                if (!ensure)
                {
                    throw new DirectoryNotFoundException();
                }

                file.Directory.Create();
            }
            
            if (!file.Exists)
            {
                if (!ensure)
                {
                    throw new FileNotFoundException
                    {
                        Source = file.FullName
                    };
                }

                file.Create().Close();
            }
                
            file.Refresh();

            using (var fs = file.OpenRead())
            {
                var reader = new BinaryFileReader(new BinaryReader(fs));

                try
                {
                    deserializer(reader);
                }
                catch (EndOfStreamException eos)
                {
                    if (file.Length > 0)
                    {
                        Console.WriteLine("[Persistence]: {0}", eos);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("[Persistence]: {0}", e);
                }
                finally
                {
                    reader.Close();
                }
            }
        }
    }
}
