/* ***************************************************************************
 * GumpProfile.cs
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

namespace Server.Diagnostics {
    public class GumpProfile : BaseProfile {
        private static Dictionary<Type, GumpProfile> _profiles = new Dictionary<Type, GumpProfile>();

        public static IEnumerable<GumpProfile> Profiles {
            get {
                lock ( _profiles ) {
                    return new List<GumpProfile>( _profiles.Values );
                }
            }
        }

        public static GumpProfile Acquire( Type type ) {
            if ( !Core.Profiling ) {
                return null;
            }

            lock ( _profiles ) {
                GumpProfile prof;

                if ( !_profiles.TryGetValue( type, out prof ) ) {
                    _profiles.Add( type, prof = new GumpProfile( type ) );
                }

                return prof;
            }
        }

        public GumpProfile( Type type )
            : base( type.FullName ) {
        }
    }
}
