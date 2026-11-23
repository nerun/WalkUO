/* ***************************************************************************
 * AcidSlime.cs
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
using System;
using Server;
using Server.Mobiles;
using Server.Spells;
using System.Collections;
using System.Collections.Generic;

namespace Server.Items
{
    public class AcidSlime : Item
    {
        private TimeSpan m_Duration;
        private int m_MinDamage;
        private int m_MaxDamage;
        private DateTime m_Created;
        private bool m_Drying;
        private Timer m_Timer;

        [Constructable]
        public AcidSlime() : this( TimeSpan.FromSeconds( 10.0 ), 5, 10 )
        {
        }

        public override string DefaultName { get { return "slime"; } }

        [Constructable]
        public AcidSlime( TimeSpan duration, int minDamage, int maxDamage )
            : base( 0x122A )
        {
            Hue = 0x3F;
            Movable = false;
            m_MinDamage = minDamage;
            m_MaxDamage = maxDamage;
            m_Created = DateTime.UtcNow;
            m_Duration = duration;
            m_Timer = Timer.DelayCall( TimeSpan.Zero, TimeSpan.FromSeconds( 1 ), new TimerCallback( OnTick ) );
        }

        public override void OnAfterDelete()
        {
            if( m_Timer != null )
                m_Timer.Stop();
        }

        private void OnTick()
        {
            DateTime now = DateTime.UtcNow;
            TimeSpan age = now - m_Created;

            if( age > m_Duration ) {
                Delete();
            } else {
                if( !m_Drying && age > (m_Duration - age) )
                {
                    m_Drying = true;
                    ItemID = 0x122B;
                }

                List<Mobile> toDamage = new List<Mobile>();

                foreach( Mobile m in GetMobilesInRange( 0 ) )
                {
                    BaseCreature bc = m as BaseCreature;
                    if( m.Alive && !m.IsDeadBondedPet && (bc == null || bc.Controlled || bc.Summoned) )
                    {
                        toDamage.Add( m );
                    }
                }

                for ( int i = 0; i < toDamage.Count; i++ )
                    Damage( toDamage[i] );
            }
        }

        public override bool OnMoveOver( Mobile m )
        {
            Damage( m );
            return true;
        }

        public void Damage ( Mobile m )
        {
            int damage = Utility.RandomMinMax( m_MinDamage, m_MaxDamage );
            if ( Core.AOS )
                AOS.Damage( m, damage, 0, 0, 0, 100, 0 );
            else
                m.Damage( damage );
        }

        public AcidSlime( Serial serial ) : base( serial )
        {
        }

        public override void Serialize( GenericWriter writer )
        {
        }

        public override void Deserialize( GenericReader reader )
        {
            // Nothing was saved; an item restored from a save has no timer and would never expire.
            Delete();
        }
    }
}
