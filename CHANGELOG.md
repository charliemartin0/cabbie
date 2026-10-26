# Changelog

## 0.1.0 (2026-10-26)


### Features

* add aws sqs transport ([b525e02](https://github.com/charliemartin0/cabbie/commit/b525e02e0adcf77f78dd5eb4ebb4ef5d91d34207))
* add aws sqs transport ([d9e5274](https://github.com/charliemartin0/cabbie/commit/d9e52747f221793675e64ccf7b2d9d7e3aa2688e))
* add aws sqs transport ([475f2ef](https://github.com/charliemartin0/cabbie/commit/475f2efbfd9e3997750a4ba00ecc8954ccf335d5))
* add durable postgres scheduler ([9a0624a](https://github.com/charliemartin0/cabbie/commit/9a0624ad8f64f31e23d5c98f766912068b62d76b))
* add durable postgres scheduler ([ae5f5a9](https://github.com/charliemartin0/cabbie/commit/ae5f5a9d875b778d06c64e6e1c78ce5b9493ea16))
* add ef core transactional outbox and relay ([7602d44](https://github.com/charliemartin0/cabbie/commit/7602d44fe4adee423786aa273863076e83c68ee0))
* add ef core transactional outbox and relay ([7293185](https://github.com/charliemartin0/cabbie/commit/7293185fa55ae52ae1505a2e134284829ef96c19))
* add sns publish transport to cabbie.aws ([5142063](https://github.com/charliemartin0/cabbie/commit/5142063b677ffbd1edd140a6033ccfca950ed52f))
* add sns publish transport to cabbie.aws ([fe6578f](https://github.com/charliemartin0/cabbie/commit/fe6578f2b4f5f3aeddb5da6bbb8611b19d3cee13))
* add sns publish transport to cabbie.aws ([e296fe9](https://github.com/charliemartin0/cabbie/commit/e296fe9c4c27d7a65caed7a9e9772cde11e4de1d))
* dead-letter unreadable messages on the local listener ([2b162e4](https://github.com/charliemartin0/cabbie/commit/2b162e42703b256b94dc85af0b029f4cb4615dba))
* dead-letter unreadable messages on the local listener ([5c8381d](https://github.com/charliemartin0/cabbie/commit/5c8381d6ad22e0a47b1d9cd85bd85a6fe04f2dd8))
* dedupe deliveries per handler with an inbox ([3466da7](https://github.com/charliemartin0/cabbie/commit/3466da741f70c340b861420ebdf7c3c331c2caec))
* dedupe deliveries per handler with an inbox ([36fbd85](https://github.com/charliemartin0/cabbie/commit/36fbd856a297716814d56ce3c1007a53ca0be1e3))
* isolate handler failures in fan-out and make requeue per-handler ([987b1e6](https://github.com/charliemartin0/cabbie/commit/987b1e63de0ab8c5d645ee3f249495c19b7b523e))
* isolate handler failures in fan-out and make requeue per-handler ([412f4be](https://github.com/charliemartin0/cabbie/commit/412f4be36a7b5043d82649a97a9fd5e94f218eeb))


### Bug Fixes

* back off failing scheduled rows and dead-letter after max attempts ([1b25581](https://github.com/charliemartin0/cabbie/commit/1b2558178a57c08012854a7e555ce374dd546de4))
* dead-letter unreadable outbox rows so the relay moves past them ([4194d16](https://github.com/charliemartin0/cabbie/commit/4194d160e6d48aa04c532eb42ff9be2e6d434d55))
* dedupe pre-upgrade inbox rows against any handler ([3a23e65](https://github.com/charliemartin0/cabbie/commit/3a23e654fea06e0ab9b13443f9c89af2b077a5cc))
* gate pack job on build and skip empty adapter packages ([5366f3b](https://github.com/charliemartin0/cabbie/commit/5366f3b91efb3e1b4e9ada36a3e6df76ae2cd3b8))
* give each fan-out schedule retry its own envelope id ([773bd63](https://github.com/charliemartin0/cabbie/commit/773bd639101bb139e5482f63c8b3d782baef3275))
* isolate outbox relay failures per row with backoff, dead-letter cap and redrive ([86ccf8d](https://github.com/charliemartin0/cabbie/commit/86ccf8d560450fa6f79966d5886895ad94d441b0))
* keep scheduled row when dispatch fails outside the error policy ([244dc2a](https://github.com/charliemartin0/cabbie/commit/244dc2aadc3ab8ea29a19fffb2d8907c6a66ee61))
* keep sqs listener polling after unexpected receive errors ([77d3f77](https://github.com/charliemartin0/cabbie/commit/77d3f770efcdbe85fca968fcbf661e2a9b8412eb))
* keep sqs listener polling after unexpected receive errors ([00748bc](https://github.com/charliemartin0/cabbie/commit/00748bc397cf7509faadf0bef88753a29f6027a3))
* keep sqs poll loop alive on unexpected per-message faults ([00d4f87](https://github.com/charliemartin0/cabbie/commit/00d4f8700a2910bf7335aaa1842990fd074da212))
* keep sqs poll loop alive on unexpected per-message faults ([89e4045](https://github.com/charliemartin0/cabbie/commit/89e4045d0b3992851986ba29e8e816673c70dafc))
* redeliver unresolved fan-out faults per handler after a requeue ([9df31d6](https://github.com/charliemartin0/cabbie/commit/9df31d6c484a90bb3e406d48ebb966632523a91b))
* release inbox before requeue, mark processed after commit, bound in-memory inbox ([9bb8ff9](https://github.com/charliemartin0/cabbie/commit/9bb8ff95a37b9941af299ed2cb4af5bc3b37fa15))
