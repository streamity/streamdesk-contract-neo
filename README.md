NEO SmartContract for the service of exchanging fiat money for NEO and GAS.
1. receivedCoin - method should be called when sending crypto currency
2. setApprove and setCanceled - calls the service, if the payment for the fiat is successful or vice versa.
3. withdraw - the service causes the transfer of crypto currency to the recipient depending on the satato. Also, this method can lead the owner of the SC commission from transactions or what happened in SC without receiving receivedCoin, or by incorrect parameters.
4. dealInfo - allows you to find out the current state of the transaction
5. getBalance - displays the total balance, reserved and available for the owner's withdrawal.
