/* ***************************************************************************
 * BaseProfile.cs
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
using System.Diagnostics;
using System.Threading;

namespace Server.Diagnostics {
    public abstract class BaseProfile {
        public static void WriteAll<T>( TextWriter op, IEnumerable<T> profiles ) where T : BaseProfile {
            List<T> list = new List<T>( profiles );

            list.Sort( delegate( T a, T b ) {
                return -a.TotalTime.CompareTo( b.TotalTime );
            } );

            foreach ( T prof in list ) {
                prof.WriteTo( op );
                op.WriteLine();
            }
        }

        private string _name;

        private long _count;

        private TimeSpan _totalTime;
        private TimeSpan _peakTime;

        private readonly object _sync = new object();
        private ThreadLocal<Stopwatch> _stopwatch;

        public string Name {
            get {
                return _name;
            }
        }

        public long Count {
            get {
                lock ( _sync ) {
                    return _count;
                }
            }
        }

        public TimeSpan AverageTime {
            get {
                lock ( _sync ) {
                    return TimeSpan.FromTicks( _totalTime.Ticks / Math.Max( 1, _count ) );
                }
            }
        }

        public TimeSpan PeakTime {
            get {
                lock ( _sync ) {
                    return _peakTime;
                }
            }
        }

        public TimeSpan TotalTime {
            get {
                lock ( _sync ) {
                    return _totalTime;
                }
            }
        }

        protected BaseProfile( string name ) {
            _name = name;

            _stopwatch = new ThreadLocal<Stopwatch>( () => new Stopwatch() );
        }

        public virtual void Start() {
            Stopwatch stopwatch = _stopwatch.Value;

            if ( stopwatch.IsRunning ) {
                stopwatch.Reset();
            }

            stopwatch.Start();
        }

        public virtual void Finish() {
            Stopwatch stopwatch = _stopwatch.Value;
            TimeSpan elapsed = stopwatch.Elapsed;
            stopwatch.Reset();

            lock ( _sync ) {
                _totalTime += elapsed;

                if ( elapsed > _peakTime ) {
                    _peakTime = elapsed;
                }

                _count++;
            }
        }

        public virtual void WriteTo( TextWriter op ) {
            op.Write( "{0,-100} {1,12:N0} {2,12:F5} {3,-12:F5} {4,12:F5}", Name, Count, AverageTime.TotalSeconds, PeakTime.TotalSeconds, TotalTime.TotalSeconds );
        }
    }
}
