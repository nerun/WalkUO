/* ***************************************************************************
 * TimerProfile.cs
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
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading;

namespace Server.Diagnostics {
    public class TimerProfile : BaseProfile {
        private static Dictionary<string, TimerProfile> _profiles = new Dictionary<string, TimerProfile>();

        public static IEnumerable<TimerProfile> Profiles {
            get {
                lock ( _profiles ) {
                    return new List<TimerProfile>( _profiles.Values );
                }
            }
        }

        public static TimerProfile Acquire( string name ) {
            if ( !Core.Profiling ) {
                return null;
            }

            lock ( _profiles ) {
                TimerProfile prof;

                if ( !_profiles.TryGetValue( name, out prof ) ) {
                    _profiles.Add( name, prof = new TimerProfile( name ) );
                }

                return prof;
            }
        }

        private long _created, _started, _stopped;

        public long Created {
            get {
                return Interlocked.Read( ref _created );
            }
            set {
                Interlocked.Exchange( ref _created, value );
            }
        }

        public long Started {
            get {
                return Interlocked.Read( ref _started );
            }
            set {
                Interlocked.Exchange( ref _started, value );
            }
        }

        public long Stopped {
            get {
                return Interlocked.Read( ref _stopped );
            }
            set {
                Interlocked.Exchange( ref _stopped, value );
            }
        }

        internal void IncrementCreated() {
            Interlocked.Increment( ref _created );
        }

        internal void IncrementStarted() {
            Interlocked.Increment( ref _started );
        }

        internal void IncrementStopped() {
            Interlocked.Increment( ref _stopped );
        }

        public TimerProfile( string name )
            : base( name ) {
        }

        public override void WriteTo( TextWriter op ) {
            base.WriteTo( op );

            op.Write( "\t{0,12:N0} {1,12:N0} {2,-12:N0}", Created, Started, Stopped );
        }
    }
}
