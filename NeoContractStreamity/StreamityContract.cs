using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace NeoContractStreamity
{
    public class StreamityContract : SmartContract
    {
        private static readonly byte[] OWNER = "AbMCavg9QVDNGm39vzo8x9biRvFVSbxxeD".ToScriptHash();

        // Constants
        private static readonly byte[] Empty = { };
        private static readonly byte Remark_1 = 0xf1;//DealHash
        private static readonly byte Remark_2 = 0xf2;//For owner withdraw
        private static readonly byte Remark_3 = 0xf3;//Asset owner withdraw
        private static readonly byte[] OpCode_TailCall = { 0x69 };

        // Asset Type
        private static readonly byte[] AssetSystem = { 0x01 };// Asset System
        private static readonly byte[] AssetNEP5 = { 0x02 };// Asset NEP5

        // Deal status
        private static readonly byte[] STATUS_DEAL_WAIT_CONFIRMATION = { 0x01 };
        private static readonly byte[] STATUS_DEAL_CANCEL = { 0x02 };
        private static readonly byte[] STATUS_DEAL_APPROVE = { 0x03 };
        private static readonly byte[] STATUS_DEAL_RELEASE = { 0x04 };
        private static readonly byte[] STATUS_DEAL_CANCEL_RELEASE = { 0x05 };

        private static readonly uint requestCancellationTime = 2 * 60 * 60; // 2 hours

        // Byte Constants 
        private static readonly byte[] NeoAssetID = { 155, 124, 255, 218, 166, 116, 190, 174, 15, 147, 14, 190, 96, 133, 175, 144, 147, 229, 254, 86, 179, 74, 92, 34, 12, 205, 207, 110, 252, 51, 111, 197 };
        private static readonly byte[] GasAssetID = { 231, 45, 40, 105, 121, 238, 108, 177, 183, 230, 93, 253, 223, 178, 227, 132, 16, 11, 141, 20, 142, 119, 88, 222, 66, 228, 22, 139, 113, 121, 44, 96 };
        private static readonly byte[] WithdrawArgs = { 0x00, 0xc1, 0x08, 0x77, 0x69, 0x74, 0x68, 0x64, 0x72, 0x61, 0x77 }; // PUSH0, PACK, PUSHBYTES8, "withdraw" as bytes

        [DisplayName("startDealEvent")]
        public static event Action<byte[], byte[], byte[], byte[], byte[], byte[]> StartedDealEvent;

        [DisplayName("approveDealEvent")]
        public static event Action<byte[], byte[], byte[]> ApprovedDealEvent;

        [DisplayName("releaseEvent")]
        public static event Action<byte[], byte[], byte[]> ReleasedEvent;

        [DisplayName("releaseCancelEvent")]
        public static event Action<byte[], byte[], byte[]> ReleasedCancelEvent;

        [DisplayName("sellerCancelEvent")]
        public static event Action<byte[], byte[], byte[]> SellerCancellEvent;

        [DisplayName("dealRepeatedEvent")]
        public static event Action<byte[], byte[], byte[]> DealRepeatedEvent;

        [DisplayName("dealBrokenEvent")]
        public static event Action<byte[], byte[], byte[], byte[], byte[], byte[]> DealBrokenEvent;

        private struct Balance
        {
            public BigInteger allBalance;
            public BigInteger receivedBalance;
            public BigInteger difference;
        }

        private static Balance BalanceStruct(byte[] asset)
        {

            BigInteger allBalance = GetBalance(asset);
            BigInteger receivedBalance = GetReceivedBalance(asset);

            var divider = GetDivider(asset);
            return new Balance
            {
                allBalance = allBalance / divider,
                receivedBalance = receivedBalance / divider,
                difference = allBalance - receivedBalance
            };
        }

        private struct Deal
        {
            public byte[] seller;
            public byte[] buyer;
            public byte[] assetID;
            public BigInteger value;
            public BigInteger commission;
            public int cancelTime;
            public byte[] status;
            public byte[] hashDeal;
            public BigInteger valueInt;
            public BigInteger commissionInt;
        }

        private static Deal DealStruct(byte[] hashDeal)
        {
            var sellerBuyerAssetStatus = Storage.Get(Context(), hashDeal.Concat("sellerBuyerAssetStatus".AsByteArray()));
            BigInteger value = Storage.Get(Context(), hashDeal.Concat("value".AsByteArray())).AsBigInteger();
            BigInteger commission = Storage.Get(Context(), hashDeal.Concat("commission".AsByteArray())).AsBigInteger();
            var lengthAsset = 32;
            if (sellerBuyerAssetStatus.Range(40, 1) == AssetNEP5) lengthAsset = 20;

            byte[] assetID = sellerBuyerAssetStatus.Range(41, lengthAsset);
            var divider = GetDivider(assetID);

            return new Deal
            {
                seller = sellerBuyerAssetStatus.Range(0, 20),
                buyer = sellerBuyerAssetStatus.Range(20, 20),
                assetID = assetID,
                value = value,
                commission = commission,
                cancelTime = (int)Storage.Get(Context(), hashDeal.Concat("cancelTime".AsByteArray())).AsBigInteger(),
                status = sellerBuyerAssetStatus.Range(41 + lengthAsset, 1),
                hashDeal = hashDeal,
                valueInt = value / divider,
                commissionInt = commission / 1
            };
        }

        public static object Main(string operation, params object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)
            {
                var tx = ExecutionEngine.ScriptContainer as Transaction;
                byte[] scriptHashContract = ExecutionEngine.ExecutingScriptHash;
                ulong valueOut = 0;
                var invocationTransaction = (InvocationTransaction)tx;
                if (invocationTransaction.Script != WithdrawArgs.Concat(OpCode_TailCall).Concat(ExecutionEngine.ExecutingScriptHash)) return false;// Check call app

                //owner withdraw
                if (IsOwnerWithdraw(tx) == true)
                {
                    byte[] assetID = IsOwnerWithdrawAsset(tx);
                    if (assetID == Empty) return false;
                    valueOut = CheckWithdrawalOutputs(tx, OWNER, scriptHashContract, assetID);

                    BigInteger allBalance = GetBalance(assetID);
                    BigInteger receivedBalance = GetReceivedBalance(assetID);
                    BigInteger withdrawAvalible = allBalance - receivedBalance;

                    if (valueOut != 0 && withdrawAvalible >= valueOut)
                        return true;
                    else
                        return false;
                }
                else
                {
                    var hashDeal = GetHashDeal(tx);
                    if (hashDeal == Empty) return false;
                    Deal deal = DealStruct(hashDeal);
                    if (deal.status == Empty) return false;

                    byte[] recipient = GetRecipient(deal);
                    if (recipient == Empty) return false;

                    if (CheckWithdrawalOutputsDeal(tx, recipient, scriptHashContract, deal) == false) return false;

                    return true;
                }
            }
            else if (Runtime.Trigger == TriggerType.VerificationR)
            {
                return true;
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                if (operation == "getBalance")
                {
                    return BalanceStruct((byte[])args[0]);
                }
                if (operation == "dealInfo")
                {
                    return DealStruct((byte[])args[0]);
                }
                if (operation == "getHashDeal")
                {
                    return GetHashDeal((byte[])args[0], (byte[])args[1], (byte[])args[2], (byte[])args[3], (BigInteger)args[4], (BigInteger)args[5]);
                }
                if (operation == "receivedCoin")
                {
                    return ReceivedCoin((byte[])args[0], (byte[])args[1], (byte[])args[2], (byte[])args[3], (BigInteger)args[4], (BigInteger)args[5]);
                }
                if (operation == "setApprove")
                {
                    return SetApprove((byte[])args[0]);
                }
                if (operation == "setCanceled")
                {
                    return SetCanceled((byte[])args[0]);
                }
                if (operation == "withdraw")
                {
                    return ProcessWithdrawal();
                }
                if (operation == "deleteDeal")
                {
                    return DeleteDeal((byte[])args[0]);
                }
            }

            return false;
        }

        private static bool ProcessWithdrawal()
        {
            var tx = (Transaction)ExecutionEngine.ScriptContainer;
            byte[] scriptHashContract = ExecutionEngine.ExecutingScriptHash;
            ulong valueOut = 0;
            if (IsOwnerWithdraw(tx) == true)
            {
                byte[] assetID = IsOwnerWithdrawAsset(tx);
                if (assetID == Empty) return false;
                valueOut = CheckWithdrawalOutputs(tx, OWNER, scriptHashContract, assetID);
                BigInteger allBalance = GetBalance(assetID);
                BigInteger receivedBalance = GetReceivedBalance(assetID);
                BigInteger withdrawAvalible = allBalance - receivedBalance;

                if (valueOut != 0 && withdrawAvalible >= valueOut) return true;
                else return false;
            }
            else
            {
                var hashDeal = GetHashDeal(tx);
                if (hashDeal == Empty) return false;
                Deal deal = DealStruct(hashDeal);

                byte[] recipient = GetRecipient(deal);
                if (recipient == Empty) return false;

                if (CheckWithdrawalOutputsDeal(tx, recipient, scriptHashContract, deal) == false) return false;

                UpdateReceivedBalance(deal.assetID, deal.value, false);
                if (deal.assetID == NeoAssetID && deal.status == STATUS_DEAL_CANCEL)
                    UpdateReceivedBalance(GasAssetID, deal.commission, false);

                if (deal.status == STATUS_DEAL_APPROVE)
                {
                    ChangeStatus(deal, STATUS_DEAL_RELEASE);
                    ReleasedEvent(hashDeal, deal.seller, deal.buyer);
                }
                else
                {
                    ChangeStatus(deal, STATUS_DEAL_CANCEL_RELEASE);
                    ReleasedCancelEvent(hashDeal, deal.seller, deal.buyer);
                }
                return true;
            }
        }

        private static ulong CheckWithdrawalOutputs(Transaction tx, byte[] recipient, byte[] scriptHashContract, byte[] assetId)
        {
            ulong valueOut = 0;
            foreach (var output in tx.GetOutputs())
            {
                if (output.ScriptHash != recipient && output.ScriptHash != scriptHashContract) return 0;
                if (output.ScriptHash == scriptHashContract) continue;
                if (output.AssetId != assetId) return 0;

                valueOut = valueOut += (ulong)output.Value;
            }
            return valueOut;
        }

        private static bool CheckWithdrawalOutputsDeal(Transaction tx, byte[] recipient, byte[] scriptHashContract, Deal deal)
        {
            ulong valueNEO = 0;
            ulong valueOut = 0;
            foreach (var output in tx.GetOutputs())
            {
                if (output.ScriptHash != recipient && output.ScriptHash != scriptHashContract) return false; //The recipient's address must be a buyer
                if (output.ScriptHash == scriptHashContract) continue; //If the transaction has a SC
                if (deal.assetID == NeoAssetID && deal.status == STATUS_DEAL_CANCEL)
                {
                    if (output.AssetId != NeoAssetID && output.AssetId != GasAssetID) return false;
                    if (output.AssetId == NeoAssetID) valueNEO = valueNEO += (ulong)output.Value;
                    if (output.AssetId == GasAssetID) valueOut = valueOut += (ulong)output.Value;
                }
                else
                {
                    if (output.AssetId != deal.assetID) return false; //If the transaction is in NEO and it is canceled, we return both the NEO and the commission to GAS
                    valueOut = valueOut += (ulong)output.Value;
                }
            }
            if (deal.assetID == NeoAssetID && deal.status == STATUS_DEAL_CANCEL)
            {
                if (valueNEO != deal.value) return false; //The amount withdrawn must be equal to the amount in the transaction
                if (valueOut != deal.commission) return false; //The amount withdrawn commission must be equal to the amount in the transaction
            }
            else
            {
                if (valueOut != deal.value) return false; //The amount withdrawn must be equal to the amount in the transaction
            }

            return true;
        }

        private static object GetHashDeal(byte[] dealId, byte[] seller, byte[] buyer, byte[] assetID, BigInteger value, BigInteger commission)
        {
            byte[] hashDeal = Hash256(dealId.Concat(seller).Concat(buyer).Concat(value.AsByteArray()).Concat(commission.ToByteArray()));
            return hashDeal;
        }

        private static object ReceivedCoin(byte[] dealId, byte[] seller, byte[] buyer, byte[] assetID, BigInteger value, BigInteger commission)
        {
            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            BigInteger receivedNEO = 0;
            BigInteger receivedGAS = 0;
            BigInteger receivedNEP5 = 0;

            byte[] hashDeal = Hash256(dealId.Concat(seller).Concat(buyer).Concat(value.AsByteArray()).Concat(commission.ToByteArray()));

            Deal deal = DealStruct(hashDeal);
            if (deal.status != Empty)
            {
                DealRepeatedEvent(hashDeal, seller, buyer);
                return false;
            }

            foreach (TransactionOutput output in tx.GetOutputs())
            {
                if (output.ScriptHash == ExecutionEngine.ExecutingScriptHash)
                {
                    ulong receivedValue = (ulong)output.Value;
                    if (output.AssetId == NeoAssetID)
                    {
                        receivedNEO += receivedValue;
                    }
                    else if (output.AssetId == GasAssetID)
                    {
                        receivedGAS += receivedValue;
                    }
                    else if (output.AssetId == assetID)
                    {
                        receivedNEP5 += receivedValue;
                    }
                }
            }
            bool checkValues = true;
            BigInteger receivedTokens = 0;

            if (assetID == NeoAssetID)
            {
                if (value != receivedNEO || commission != receivedGAS) checkValues = false; //If the transaction is in NEO, then value should be = NEO, commission = GAS
                else receivedTokens = receivedNEO;
            }
            else if (assetID == GasAssetID)
            {
                if (value != receivedGAS) checkValues = false;
                else receivedTokens = receivedGAS;
            }
            else
            {
                checkValues = false;
            }

            if (checkValues == false)
            {
                DealBrokenEvent(hashDeal, seller, buyer, assetID, value.AsByteArray(), commission.AsByteArray());//If the transaction has inconsistencies, end processing and create an Event
                return false;
            }

            UpdateReceivedBalance(assetID, receivedTokens, true);//Increase the reserved balance, without the possibility of withdrawal by the owner
            if(assetID == NeoAssetID)
                UpdateReceivedBalance(GasAssetID, receivedGAS, true);

            StartDealForUser(hashDeal, dealId, seller, buyer, assetID, value, commission);
            return true;
        }

        private static bool SetApprove(byte[] hashDeal)
        {
            if (!IsOwner()) return false;
            Deal deal = DealStruct(hashDeal);
            if (deal.status == STATUS_DEAL_WAIT_CONFIRMATION)
            {
                if (deal.assetID == NeoAssetID)
                {
                    UpdateReceivedBalance(GasAssetID, deal.commission, false);
                }
                else if (deal.assetID == GasAssetID)
                {
                    UpdateReceivedBalance(deal.assetID, deal.commission, false);
                    SetDeal(hashDeal, "value", deal.value - deal.commission);
                }
                else
                {
                    return false;
                }

                ChangeStatus(deal, STATUS_DEAL_APPROVE);

                ApprovedDealEvent(hashDeal, deal.seller, deal.buyer);

                return true;
            }
            return false;
        }

        private static bool SetCanceled(byte[] hashDeal)
        {
            if (!IsOwner()) return false;
            Deal deal = DealStruct(hashDeal);

            if (deal.status == STATUS_DEAL_WAIT_CONFIRMATION && deal.cancelTime < Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp)
            {
                ChangeStatus(deal, STATUS_DEAL_CANCEL);
                SellerCancellEvent(hashDeal, deal.seller, deal.buyer);
                return true;
            }
            return false;
        }

        private static void StartDealForUser(byte[] hashDeal, byte[] dealId, byte[] seller, byte[] buyer, byte[] assetID, BigInteger value, BigInteger commission)
        {
            SetSellerBuyerAssetStatus(hashDeal, seller, buyer, assetID, STATUS_DEAL_WAIT_CONFIRMATION);
            SetDeal(hashDeal, "value", value.AsByteArray());
            SetDeal(hashDeal, "commission", commission.AsByteArray());
            SetDeal(hashDeal, "cancelTime", Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp + requestCancellationTime);
            StartedDealEvent(hashDeal, seller, buyer, assetID, value.AsByteArray(), commission.AsByteArray());
        }

        private static void SetSellerBuyerAssetStatus(byte[] hashDeal, byte[] seller, byte[] buyer, byte[] assetID, byte[] status)
        {
            byte[] assetType = AssetSystem;
            if (assetID.Length == 20) assetType = AssetNEP5;
            SetDeal(hashDeal, "sellerBuyerAssetStatus", seller.Concat(buyer).Concat(assetType).Concat(assetID).Concat(status));
        }

        private static void ChangeStatus(Deal deal, byte[] status)
        {
            SetSellerBuyerAssetStatus(deal.hashDeal, deal.seller, deal.buyer, deal.assetID, status);
        }

        private static void SetDeal(byte[] hashDeal, string key, object value)
        {
            Storage.Put(Context(), hashDeal.Concat(key.AsByteArray()), (byte[])value);
        }

        private static byte[] GetHashDeal(Transaction transaction)
        {
            var txnAttributes = transaction.GetAttributes();
            foreach (var attr in txnAttributes)
            {
                if (attr.Usage == Remark_1) return attr.Data;
            }
            return Empty;
        }

        private static bool IsOwnerWithdraw(Transaction transaction)
        {
            var txnAttributes = transaction.GetAttributes();
            foreach (var attr in txnAttributes)
            {
                if (attr.Usage == Remark_2 && attr.Data == OWNER) return true;
            }
            return false;
        }

        private static byte[] IsOwnerWithdrawAsset(Transaction transaction)
        {
            var txnAttributes = transaction.GetAttributes();
            foreach (var attr in txnAttributes)
            {
                if (attr.Usage == Remark_3) return attr.Data;
            }
            return Empty;
        }

        private static byte[] GetRecipient(Deal deal)
        {
            if (deal.status == STATUS_DEAL_APPROVE)
            {
                return deal.buyer;
            }
            else if (deal.status == STATUS_DEAL_CANCEL)
            {
                return deal.seller;
            }

            return Empty;
        }

        private static bool DeleteDeal(byte[] hashDeal)
        {
            if (!IsOwner()) return false;
            Deal deal = DealStruct(hashDeal);

            if(deal.status == STATUS_DEAL_RELEASE || deal.status == STATUS_DEAL_CANCEL_RELEASE)
            {
                Storage.Delete(Context(), hashDeal.Concat("sellerBuyerAssetStatus".AsByteArray()));
                Storage.Delete(Context(), hashDeal.Concat("value".AsByteArray()));
                Storage.Delete(Context(), hashDeal.Concat("commission".AsByteArray()));
                Storage.Delete(Context(), hashDeal.Concat("cancelTime".AsByteArray()));
            }
            return true;
        }

        // Returns the balance for a specific AssetID
        private static BigInteger GetBalance(byte[] assetID)
        {
            Account account = Blockchain.GetAccount(ExecutionEngine.ExecutingScriptHash);
            return account.GetBalance(assetID);
        }

        private static BigInteger GetReceivedBalance(byte[] assetID)
        {
            return Storage.Get(Context(), assetID.Concat("balance".AsByteArray())).AsBigInteger();
        }

        private static void UpdateReceivedBalance(byte[] assetID, BigInteger value, bool plus)
        {
            BigInteger currentBalance = GetReceivedBalance(assetID);
            if (plus)
            {
                Storage.Put(Context(), assetID.Concat("balance".AsByteArray()), (currentBalance + value));
            }
            else
            {
                Storage.Put(Context(), assetID.Concat("balance".AsByteArray()), (currentBalance - value));
            }
        }

        // Helpers
        private static StorageContext Context() => Storage.CurrentContext;
        private static bool IsOwner() => Runtime.CheckWitness(OWNER);
        private static byte[] GetState() => Storage.Get(Context(), "state");

        private static int GetDivider(byte[] assetID)
        {
            if (assetID == NeoAssetID) return 100000000;
            return 1;
        }
    }
}
