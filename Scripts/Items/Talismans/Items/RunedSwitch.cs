/* ***************************************************************************
 * RunedSwitch.cs
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
using Server.Targeting;

namespace Server.Items
{
    public class RunedSwitch : Item
    {
        public override int LabelNumber{ get{ return 1072896; } } // runed switch

        [Constructable]
        public RunedSwitch() : base( 0x2F61 )
        {
            Weight = 1.0;
        }

        public RunedSwitch( Serial serial ) : base( serial )
        {
        }

        public override void OnDoubleClick( Mobile from )
        {
            if ( IsChildOf( from.Backpack ) )
            {
                from.SendLocalizedMessage( 1075101 ); // Please select an item to recharge.
                from.Target = new InternalTarget( this );
            }
            else
                from.SendLocalizedMessage( 1060640 ); // The item must be in your backpack to use it.
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
        
        private class InternalTarget : Target
        {
            private RunedSwitch m_Item;

            public InternalTarget( RunedSwitch item ) : base( 0, false, TargetFlags.None )
            {
                m_Item = item;
            }

            protected override void OnTarget( Mobile from, object o )
            {
                if ( m_Item == null || m_Item.Deleted )
                    return;

                if ( !m_Item.IsChildOf( from.Backpack ) )
                {
                    from.SendLocalizedMessage( 1042001 ); // That must be in your pack for you to use it.
                    return;
                }

                if ( o is BaseTalisman )
                {
                    BaseTalisman talisman = (BaseTalisman) o;

                    if ( talisman.Charges == 0 )
                    {
                        talisman.Charges = talisman.MaxCharges;
                        m_Item.Delete();
                        from.SendLocalizedMessage( 1075100 ); // The item has been recharged.
                    }
                    else
                        from.SendLocalizedMessage( 1075099 ); // You cannot recharge that item until all of its current charges have been used.
                }
                else
                    from.SendLocalizedMessage( 1046439 ); // That is not a valid target.
            }
        }
    }
}

