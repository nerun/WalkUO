/* ***************************************************************************
 * OrangePetals.cs
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
using System.Collections;
using Server;
using Server.Mobiles;
using Server.Network;

namespace Server.Items
{
    public class OrangePetals : Item
    {
        public override int LabelNumber { get { return 1053122; } } // orange petals

        public override double DefaultWeight
        {
            get { return 0.1; }
        }

        [Constructable]
        public OrangePetals() : this( 1 )
        {
        }

        [Constructable]
        public OrangePetals( int amount ) : base( 0x1021 )
        {
            Stackable = true;
            Hue = 0x2B;
            Amount = amount;
        }

        public OrangePetals( Serial serial ) : base( serial )
        {
        }

        public override bool CheckItemUse( Mobile from, Item item ) 
        { 
            if ( item != this )
                return base.CheckItemUse( from, item );

            if ( from != this.RootParent )
            {
                from.SendLocalizedMessage( 1042038 ); // You must have the object in your backpack to use it.
                return false;
            }

            return base.CheckItemUse( from, item );
        }

        public override void OnDoubleClick( Mobile from )
        {
            if ( !IsChildOf( from.Backpack ) )
            {
                from.SendLocalizedMessage( 1042001 ); // That must be in your pack for you to use it.
                return;
            }

            OrangePetalsContext context = GetContext( from );

            if ( context != null )
            {
                from.LocalOverheadMessage( MessageType.Regular, 0x3B2, 1061904 );
                return;
            }

            from.LocalOverheadMessage( MessageType.Regular, 0x3B2, 1061905 );
            from.PlaySound( 0x3B );

            Timer timer = new OrangePetalsTimer( from );
            timer.Start();

            AddContext( from, new OrangePetalsContext( timer ) );

            this.Consume();
        }

        private static Hashtable m_Table = new Hashtable();

        private static void AddContext( Mobile m, OrangePetalsContext context )
        {
            m_Table[m] = context;
        }

        public static void RemoveContext( Mobile m )
        {
            OrangePetalsContext context = GetContext( m );

            if ( context != null )
                RemoveContext( m, context );
        }

        private static void RemoveContext( Mobile m, OrangePetalsContext context )
        {
            m_Table.Remove( m );

            context.Timer.Stop();
        }

        private static OrangePetalsContext GetContext( Mobile m )
        {
            return ( m_Table[m] as OrangePetalsContext );
        }

        public static bool UnderEffect( Mobile m )
        {
            return ( GetContext( m ) != null );
        }

        private class OrangePetalsTimer : Timer
        {
            private Mobile m_Mobile;

            public OrangePetalsTimer( Mobile from ) : base ( TimeSpan.FromMinutes( 5.0 ) )
            {
                m_Mobile = from;
            }

            protected override void OnTick()
            {
                if ( !m_Mobile.Deleted )
                {
                    m_Mobile.LocalOverheadMessage( MessageType.Regular, 0x3F, true,
                        "* You feel the effects of your poison resistance wearing off *" );
                }

                RemoveContext( m_Mobile );
            }
        }

        private class OrangePetalsContext
        {
            private Timer m_Timer;

            public Timer Timer{ get{ return m_Timer; } }

            public OrangePetalsContext( Timer timer )
            {
                m_Timer = timer;
            }
        }

        public override void Serialize( GenericWriter writer )
        {
            base.Serialize( writer );

            writer.Write( (int) 0 ); // version
        }

        public override void Deserialize( GenericReader reader )
        {
            base.Deserialize( reader );

            int version = reader.ReadInt();
        }
    }
}
