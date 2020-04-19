This fork updates the LND package to use the schema in the version of LND matching the LND subpackage.

It doesn't include `listunspent` and `channelbackups` which are available over REST in 0.10.

It also doesn't support [sendtoroute](https://groups.google.com/a/lightning.engineering/forum/#!topic/lnd/UoyCGu-RvnM), which is only available over gRPC.
