/* ***************************************************************************
 * DynamicSaveStrategy.cs
 *
 * RunUO is an open-source server emulator for Ultima Online.
 * Copyright (C) 2010  The RunUO Software Team
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
﻿/***************************************************************************
 *                          DynamicSaveStrategy.cs
 *                            -------------------
 *   begin                : December 16, 2010
 *   copyright            : (C) The RunUO Software Team
 *   email                : info@runuo.com
 *
 *   $Id$
 *
 ***************************************************************************/

/***************************************************************************
 *
 *   This program is free software; you can redistribute it and/or modify
 *   it under the terms of the GNU General Public License as published by
 *   the Free Software Foundation; either version 2 of the License, or
 *   (at your option) any later version.
 *
 ***************************************************************************/


using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.ExceptionServices;

using Server;
using Server.Guilds;

namespace Server
{
    public sealed class DynamicSaveStrategy : SaveStrategy
    {
        public override string Name { get { return "Dynamic"; } }

        private SaveMetrics _metrics;

        private SequentialFileWriter _itemData, _itemIndex;
        private SequentialFileWriter _mobileData, _mobileIndex;
        private SequentialFileWriter _guildData, _guildIndex;

        private ConcurrentBag<Item> _decayBag;

        private BlockingCollection<QueuedMemoryWriter> _itemThreadWriters;
        private BlockingCollection<QueuedMemoryWriter> _mobileThreadWriters;
        private BlockingCollection<QueuedMemoryWriter> _guildThreadWriters;

        public DynamicSaveStrategy()
        {
            _decayBag = new ConcurrentBag<Item>();
        }

        public override void Save(SaveMetrics metrics, bool permitBackgroundWrite)
        {
            this._metrics = metrics;

            Task[] saveTasks = new Task[3];
            bool backgroundWriteScheduled = false;
            Exception saveError = null;

            try
            {
                _itemThreadWriters = new BlockingCollection<QueuedMemoryWriter>();
                _mobileThreadWriters = new BlockingCollection<QueuedMemoryWriter>();
                _guildThreadWriters = new BlockingCollection<QueuedMemoryWriter>();

                OpenFiles();

                saveTasks[0] = SaveItems();
                saveTasks[1] = SaveMobiles();
                saveTasks[2] = SaveGuilds();

                SaveTypeDatabases();

                if (permitBackgroundWrite)
                {
                    //This option makes it finish the writing to disk in the background, continuing even after Save() returns.
                    Task.Factory.ContinueWhenAll(saveTasks, FinishBackgroundSave);
                    backgroundWriteScheduled = true;
                }
                else
                {
                    Task.WaitAll(saveTasks);    //Waits for the completion of all of the tasks(committing to disk)
                }
            }
            catch (Exception ex)
            {
                saveError = ex;
                throw;
            }
            finally
            {
                if (!backgroundWriteScheduled)
                {
                    foreach (Task task in saveTasks)
                    {
                        if (task != null)
                        {
                            try { task.Wait(); }
                            catch { } // Preserve the original producer or commit exception.
                        }
                    }

                    try { CloseFiles(); }
                    catch
                    {
                        if (saveError == null)
                            throw;
                    }
                }
            }
        }

        private void FinishBackgroundSave(Task[] tasks)
        {
            Exception error = null;

            try { Task.WaitAll(tasks); }
            catch (Exception ex) { error = ex; }

            try { CloseFiles(); }
            catch (Exception ex)
            {
                if (error == null)
                    error = ex;
            }

            if (error != null)
                World.NotifyDiskWriteFailed(error);
            else
                World.NotifyDiskWriteComplete();
        }

        private Task StartCommitTask(BlockingCollection<QueuedMemoryWriter> threadWriter, SequentialFileWriter data, SequentialFileWriter index)
        {
            Task commitTask = Task.Factory.StartNew(() =>
            {
                while (!(threadWriter.IsCompleted))
                {
                    QueuedMemoryWriter writer;

                    try
                    {
                        writer = threadWriter.Take();
                    }
                    catch (InvalidOperationException)
                    {
                        //Per MSDN, it's fine if we're here, successful completion of adding can rarely put us into this state.
                        break;
                    }

                    try { writer.CommitTo(data, index); }
                    catch
                    {
                        try { writer.Close(); }
                        catch { } // Preserve the original commit error.
                        throw;
                    }
                }
            });

            return commitTask;
        }

        private Task RunProducer(BlockingCollection<QueuedMemoryWriter> writers, SequentialFileWriter data, SequentialFileWriter index, Action produce)
        {
            Task commitTask = StartCommitTask(writers, data, index);
            bool producerCompleted = false;

            try
            {
                produce();
                producerCompleted = true;
            }
            finally
            {
                writers.CompleteAdding();

                if (!producerCompleted)
                {
                    try { commitTask.Wait(); }
                    catch { } // Do not replace the producer exception with a commit failure.
                }
            }

            return commitTask;
        }

        private Task SaveItems()
        {
            return RunProducer(_itemThreadWriters, _itemData, _itemIndex, ProduceItems);
        }

        private void ProduceItems()
        {
            IEnumerable<Item> items = World.Items.Values;

            //Start the producer.
            Parallel.ForEach(items, () => new QueuedMemoryWriter(), 
                (Item item, ParallelLoopState state, QueuedMemoryWriter writer) =>
                {
                    long startPosition = writer.Position;

                    item.Serialize(writer);

                    int size = (int)(writer.Position - startPosition);

                    writer.QueueForIndex(item, size);

                    if (item.Decays && item.Parent == null && item.Map != Map.Internal && DateTime.UtcNow > (item.LastMoved + item.DecayTime))
                    {
                        _decayBag.Add(item);
                    }

                    if (_metrics != null)
                    {
                        _metrics.OnItemSaved(size);
                    }

                    return writer;
                },
                (writer) =>
                {
                    try
                    {
                        writer.Flush();
                        _itemThreadWriters.Add(writer);
                    }
                    catch
                    {
                        try { writer.Close(); }
                        catch { } // Preserve the producer error.
                        throw;
                    }
                });
        }

        private Task SaveMobiles()
        {
            return RunProducer(_mobileThreadWriters, _mobileData, _mobileIndex, ProduceMobiles);
        }

        private void ProduceMobiles()
        {
            IEnumerable<Mobile> mobiles = World.Mobiles.Values;

            //Start the producer.
            Parallel.ForEach(mobiles, () => new QueuedMemoryWriter(),
                (Mobile mobile, ParallelLoopState state, QueuedMemoryWriter writer) =>
                {
                    long startPosition = writer.Position;

                    mobile.Serialize(writer);

                    int size = (int)(writer.Position - startPosition);

                    writer.QueueForIndex(mobile, size);

                    if (_metrics != null)
                    {
                        _metrics.OnMobileSaved(size);
                    }

                    return writer;
                },
                (writer) =>
                {
                    try
                    {
                        writer.Flush();
                        _mobileThreadWriters.Add(writer);
                    }
                    catch
                    {
                        try { writer.Close(); }
                        catch { } // Preserve the producer error.
                        throw;
                    }
                });
        }

        private Task SaveGuilds()
        {
            return RunProducer(_guildThreadWriters, _guildData, _guildIndex, ProduceGuilds);
        }

        private void ProduceGuilds()
        {
            IEnumerable<BaseGuild> guilds = BaseGuild.List.Values;

            //Start the producer.
            Parallel.ForEach(guilds, () => new QueuedMemoryWriter(),
                (BaseGuild guild, ParallelLoopState state, QueuedMemoryWriter writer) =>
                {
                    long startPosition = writer.Position;

                    guild.Serialize(writer);

                    int size = (int)(writer.Position - startPosition );

                    writer.QueueForIndex(guild, size);

                    if (_metrics != null)
                    {
                        _metrics.OnGuildSaved(size);
                    }

                    return writer;
                },
                (writer) =>
                {
                    try
                    {
                        writer.Flush();
                        _guildThreadWriters.Add(writer);
                    }
                    catch
                    {
                        try { writer.Close(); }
                        catch { } // Preserve the producer error.
                        throw;
                    }
                });
        }

        public override void ProcessDecay()
        {
            Item item;

            while( _decayBag.TryTake( out item ) )
            {
                if( item.OnDecay() )
                {
                    item.Delete();
                }
            }
        }

        private void OpenFiles()
        {
            _itemData = new SequentialFileWriter(World.ItemDataPath, _metrics);
            _itemIndex = new SequentialFileWriter(World.ItemIndexPath, _metrics);

            _mobileData = new SequentialFileWriter(World.MobileDataPath, _metrics);
            _mobileIndex = new SequentialFileWriter(World.MobileIndexPath, _metrics);

            _guildData = new SequentialFileWriter(World.GuildDataPath, _metrics);
            _guildIndex = new SequentialFileWriter(World.GuildIndexPath, _metrics);

            WriteCount(_itemIndex, World.Items.Count);
            WriteCount(_mobileIndex, World.Mobiles.Count);
            WriteCount(_guildIndex, BaseGuild.List.Count);
        }

        private void CloseFiles()
        {
            ExceptionDispatchInfo error = null;

            foreach (SequentialFileWriter file in new SequentialFileWriter[] { _itemData, _itemIndex, _mobileData, _mobileIndex, _guildData, _guildIndex })
            {
                if (file != null)
                {
                    try { file.Close(); }
                    catch (Exception ex)
                    {
                        if (error == null)
                            error = ExceptionDispatchInfo.Capture(ex);
                    }
                }
            }

            foreach (BlockingCollection<QueuedMemoryWriter> writers in new BlockingCollection<QueuedMemoryWriter>[] { _itemThreadWriters, _mobileThreadWriters, _guildThreadWriters })
            {
                if (writers != null)
                {
                    QueuedMemoryWriter writer;
                    try
                    {
                        while (writers.TryTake(out writer))
                        {
                            try { writer.Close(); }
                            catch (Exception ex)
                            {
                                if (error == null)
                                    error = ExceptionDispatchInfo.Capture(ex);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (error == null)
                            error = ExceptionDispatchInfo.Capture(ex);
                    }

                    try { writers.Dispose(); }
                    catch (Exception ex)
                    {
                        if (error == null)
                            error = ExceptionDispatchInfo.Capture(ex);
                    }
                }
            }

            if (error != null)
                error.Throw();
        }

        private void WriteCount(SequentialFileWriter indexFile, int count)
        {
            //Equiv to GenericWriter.Write( (int)count );
            byte[] buffer = new byte[4];

            buffer[0] = (byte)(count);
            buffer[1] = (byte)(count >> 8);
            buffer[2] = (byte)(count >> 16);
            buffer[3] = (byte)(count >> 24);

            indexFile.Write(buffer, 0, buffer.Length);
        }

        private void SaveTypeDatabases()
        {
            SaveTypeDatabase(World.ItemTypesPath, World.m_ItemTypes);
            SaveTypeDatabase(World.MobileTypesPath, World.m_MobileTypes);
        }

        private void SaveTypeDatabase(string path, List<Type> types)
        {
            BinaryFileWriter bfw = new BinaryFileWriter(path, false);

            ExceptionDispatchInfo error = null;

            try
            {
                bfw.Write(types.Count);

                foreach (Type type in types)
                {
                    bfw.Write(type.FullName);
                }

                bfw.Flush();
            }
            catch (Exception ex)
            {
                error = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                try { bfw.Close(); }
                catch
                {
                    if (error == null)
                        throw;
                }
            }

            if (error != null)
                error.Throw();
        }
    }

}
