/* ***************************************************************************
 * TargetProfile.cs
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
    public class TargetProfile : BaseProfile {
        private static Dictionary<Type, TargetProfile> _profiles = new Dictionary<Type, TargetProfile>();

        public static IEnumerable<TargetProfile> Profiles {
            get {
                lock ( _profiles ) {
                    return new List<TargetProfile>( _profiles.Values );
                }
            }
        }

        public static TargetProfile Acquire( Type type ) {
            if ( !Core.Profiling ) {
                return null;
            }

            lock ( _profiles ) {
                TargetProfile prof;

                if ( !_profiles.TryGetValue( type, out prof ) ) {
                    _profiles.Add( type, prof = new TargetProfile( type ) );
                }

                return prof;
            }
        }

        public TargetProfile( Type type )
            : base( type.FullName ) {
        }
    }
}
