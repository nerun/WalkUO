/* ***************************************************************************
 * DawnsMusicGear.cs
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
﻿using System;
using Server.Targeting;

namespace Server.Items
{
    [Flipable( 0x1053, 0x1054 )]
    public class DawnsMusicGear : Item
    {
        public static DawnsMusicGear RandomCommon
        {
            get { return new DawnsMusicGear( DawnsMusicBox.RandomTrack( DawnsMusicRarity.Common ) ); }
        }

        public static DawnsMusicGear RandomUncommon
        {
            get { return new DawnsMusicGear( DawnsMusicBox.RandomTrack( DawnsMusicRarity.Uncommon ) ); }
        }

        public static DawnsMusicGear RandomRare
        {
            get { return new DawnsMusicGear( DawnsMusicBox.RandomTrack( DawnsMusicRarity.Rare ) ); }
        }

        private MusicName m_Music;

        [CommandProperty( AccessLevel.GameMaster )]
        public MusicName Music
        {
            get { return m_Music; }
            set { m_Music = value; }
        }

        [Constructable]
        public DawnsMusicGear() : this( DawnsMusicBox.RandomTrack( DawnsMusicRarity.Common ) )
        {
        }

        [Constructable]
        public DawnsMusicGear( MusicName music ) : base( 0x1053 )
        {
            m_Music = music;

            Weight = 1.0;
        }

        public DawnsMusicGear( Serial serial ) : base( serial )
        {
        }

        public override void AddNameProperty( ObjectPropertyList list )
        {
            DawnsMusicInfo info = DawnsMusicBox.GetInfo( m_Music );

            if ( info != null )
            {
                if ( info.Rarity == DawnsMusicRarity.Common )
                    list.Add( 1075204 ); // Gear for Dawn's Music Box (Common)
                else if ( info.Rarity == DawnsMusicRarity.Uncommon )
                    list.Add( 1075205 ); // Gear for Dawn's Music Box (Uncommon)
                else if ( info.Rarity == DawnsMusicRarity.Rare )
                    list.Add( 1075206 ); // Gear for Dawn's Music Box (Rare)

                list.Add( info.Name );
            }
            else
                base.AddNameProperty( list );
        }

        public override void OnDoubleClick( Mobile from )
        {
            if ( !IsChildOf( from.Backpack ) )
            {
                from.SendLocalizedMessage( 1042001 ); // That must be in your pack for you to use it.
                return;
            }

            from.Target = new InternalTarget( this );
        }

        public override void Serialize( GenericWriter writer )
        {
            base.Serialize( writer );

            writer.WriteEncodedInt( (int) 1 ); // version
            
            writer.Write( (int) m_Music );
        }

        public override void Deserialize( GenericReader reader )
        {
            base.Deserialize( reader );

            int version = reader.ReadEncodedInt();
            
            switch ( version )
            {
                case 1:
                {
                    m_Music = (MusicName) reader.ReadInt();
                    break;
                }
            }
            
            if ( version == 0 ) // Music wasn't serialized in version 0, pick a new track of random rarity
            {
                DawnsMusicRarity rarity;
                double rand = Utility.RandomDouble();
                
                if ( rand < 0.025 )
                    rarity = DawnsMusicRarity.Rare;
                else if ( rand < 0.225 )
                    rarity = DawnsMusicRarity.Uncommon;
                else
                    rarity = DawnsMusicRarity.Common;
                
                m_Music = DawnsMusicBox.RandomTrack( rarity );
            }
        }

        public class InternalTarget : Target
        {
            private DawnsMusicGear m_Gear;

            public InternalTarget( DawnsMusicGear gear ) : base( 2, false, TargetFlags.None )
            {
                m_Gear = gear;
            }

            protected override void OnTarget( Mobile from, object targeted )
            {
                if ( m_Gear == null || m_Gear.Deleted )
                    return;

                if ( !m_Gear.IsChildOf( from.Backpack ) )
                {
                    from.SendLocalizedMessage( 1042001 ); // That must be in your pack for you to use it.
                    return;
                }

                DawnsMusicBox box = targeted as DawnsMusicBox;

                if ( box != null )
                {
                    if ( !box.Tracks.Contains( m_Gear.Music ) )
                    {
                        box.Tracks.Add( m_Gear.Music );
                        box.InvalidateProperties();

                        m_Gear.Delete();

                        from.SendLocalizedMessage( 1071961 ); // This song has been added to the musicbox.
                    }
                    else
                        from.SendLocalizedMessage( 1071962 ); // This song track is already in the musicbox.
                }
                else
                    from.SendLocalizedMessage( 1071964 ); // Gears can only be put into a musicbox.
            }
        }
    }
}
