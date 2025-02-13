﻿// Copyright (C) 2017-2020 Ixian OU
// This file is part of Ixian DLT - www.github.com/ProjectIxian/Ixian-DLT
//
// Ixian DLT is free software: you can redistribute it and/or modify
// it under the terms of the MIT License as published
// by the Open Source Initiative.
//
// Ixian DLT is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// MIT License for more details.

using DLT.Meta;
using DLT.Network;
using IXICore;
using IXICore.Inventory;
using IXICore.Meta;
using IXICore.Network;
using IXICore.Utils;
using System;
using System.Linq;

namespace DLTNode.Inventory
{
    class InventoryCacheDLT : InventoryCache
    {
        public InventoryCacheDLT():base()
        {
            typeOptions[InventoryItemTypes.block].maxItems = 100;
            typeOptions[InventoryItemTypes.blockSignature].maxItems = 200000;
            typeOptions[InventoryItemTypes.transaction].maxItems = 600000;
            typeOptions[InventoryItemTypes.keepAlive].maxItems = 600000;
            typeOptions[InventoryItemTypes.signerPow].maxItems = 600000;
        }

        override protected bool sendInventoryRequest(InventoryItem item, RemoteEndpoint endpoint)
        {
            switch (item.type)
            {
                case InventoryItemTypes.block:
                    return handleBlock(item, endpoint);
                case InventoryItemTypes.blockSignature:
                    return handleSignature(item, endpoint);
                case InventoryItemTypes.keepAlive:
                    return handleKeepAlive(item, endpoint);
                case InventoryItemTypes.transaction:
                    CoreProtocolMessage.broadcastGetTransaction(item.hash, 0, endpoint);
                    return true;
                case InventoryItemTypes.signerPow:
                    return handleSignerPow(item, endpoint);
            }
            return false;
        }

        private bool handleBlock(InventoryItem item, RemoteEndpoint endpoint)
        {
            InventoryItemBlock iib = (InventoryItemBlock)item;
            ulong last_block_height = IxianHandler.getLastBlockHeight();
            if (iib.blockNum > last_block_height)
            {
                byte include_tx = 2;
                if(Node.isMasterNode())
                {
                    include_tx = 0;
                }
                BlockProtocolMessages.broadcastGetBlock(last_block_height + 1, null, endpoint, include_tx, true);
                if(iib.blockNum == last_block_height + 1)
                {
                    return true;
                }
            }
            return false;
        }

        private bool handleKeepAlive(InventoryItem item, RemoteEndpoint endpoint)
        {
            if(endpoint == null)
            {
                return false;
            }
            InventoryItemKeepAlive iika = (InventoryItemKeepAlive)item;
            Presence p = PresenceList.getPresenceByAddress(iika.address);
            if (p == null)
            {
                CoreProtocolMessage.broadcastGetPresence(iika.address, endpoint);
                return false;
            }
            else
            {
                var pa = p.addresses.Find(x => x.device.SequenceEqual(iika.deviceId));
                if (pa == null || iika.lastSeen > pa.lastSeenTime)
                {
                    byte[] address_len_bytes = ((ulong)iika.address.Length).GetIxiVarIntBytes();
                    byte[] device_len_bytes = ((ulong)iika.deviceId.Length).GetIxiVarIntBytes();
                    byte[] data = new byte[1 + address_len_bytes.Length + iika.address.Length + device_len_bytes.Length + iika.deviceId.Length];
                    data[0] = 1;
                    Array.Copy(address_len_bytes, 0, data, 1, address_len_bytes.Length);
                    Array.Copy(iika.address, 0, data, 1 + address_len_bytes.Length, iika.address.Length);
                    Array.Copy(device_len_bytes, 0, data, 1 + address_len_bytes.Length + iika.address.Length, device_len_bytes.Length);
                    Array.Copy(iika.deviceId, 0, data, 1 + address_len_bytes.Length + iika.address.Length + device_len_bytes.Length, iika.deviceId.Length);
                    endpoint.sendData(ProtocolMessageCode.getKeepAlives, data, null);
                    return true;
                }
            }
            return false;
        }

        private bool handleSignature(InventoryItem item, RemoteEndpoint endpoint)
        {
            InventoryItemSignature iis = (InventoryItemSignature)item;
            ulong last_block_height = IxianHandler.getLastBlockHeight();
            byte[] address = iis.address;
            ulong block_num = iis.blockNum;
            if (block_num + 5 > last_block_height && block_num <= last_block_height + 1)
            {
                if (block_num == last_block_height + 1)
                {
                    lock (Node.blockProcessor.localBlockLock)
                    {
                        Block local_block = Node.blockProcessor.localNewBlock;
                        if (local_block == null || local_block.blockNum != block_num)
                        {
                            return false;
                        }
                        if (!local_block.blockChecksum.SequenceEqual(iis.blockHash)
                            || local_block.hasNodeSignature(address))
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    Block sf_block = Node.blockChain.getBlock(block_num);
                    if (!sf_block.blockChecksum.SequenceEqual(iis.blockHash)
                        || sf_block.hasNodeSignature(address))
                    {
                        return false;
                    }
                }
                byte[] block_num_bytes = block_num.GetIxiVarIntBytes();
                byte[] addr_len_bytes = ((ulong)address.Length).GetIxiVarIntBytes();
                byte[] data = new byte[block_num_bytes.Length + 1 + addr_len_bytes.Length + address.Length];
                Array.Copy(block_num_bytes, data, block_num_bytes.Length);
                data[block_num_bytes.Length] = 1;
                Array.Copy(addr_len_bytes, 0, data, block_num_bytes.Length + 1, addr_len_bytes.Length);
                Array.Copy(address, 0, data, block_num_bytes.Length + 1 + addr_len_bytes.Length, address.Length);
                if(endpoint == null)
                {
                    CoreProtocolMessage.broadcastProtocolMessageToSingleRandomNode(new char[]{ 'M', 'H' }, ProtocolMessageCode.getSignatures, data, block_num);
                }else
                {
                    endpoint.sendData(ProtocolMessageCode.getSignatures, data, null);
                }
                return true;
            }
            return false;
        }

        private bool handleSignerPow(InventoryItem item, RemoteEndpoint endpoint)
        {
            if (endpoint == null)
            {
                return false;
            }
            InventoryItemSignerPow iisp = (InventoryItemSignerPow)item;
            Presence p = PresenceList.getPresenceByAddress(iisp.address);
            if (p == null)
            {
                CoreProtocolMessage.broadcastGetPresence(iisp.address, endpoint);
                return false;
            }
            else
            {
                CoreProtocolMessage.broadcastGetSignerPow(iisp.address, endpoint);
                return true;
            }
            return false;
        }
    }
}
