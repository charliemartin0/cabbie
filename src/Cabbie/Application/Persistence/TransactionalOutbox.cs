// Transactional outbox contract — enforced by TransactionalOutboxMiddleware (kernel).
//
// The middleware is the kernel implementation of this contract (this slice): it opens one
// IOutboxTransaction per handler attempt and exposes it as the ambient
// OutboxTransactionScope.Current. The dispatch path (MessageDelivery.DispatchOutgoing and
// the Mediator saga branch) stages cascaded envelopes on that transaction instead of
// publishing them immediately, and the call site that owns the handler attempt commits in
// a finally AFTER the saga state was saved and the immediate-publish attempt; the
// middleware rolls back when the handler throws. A throwing handler — or a failed saga
// save — therefore publishes nothing ("no save then publish").
//
// The transaction is short-lived (one per handler call) while the store is long-lived
// (one per host); obtain it from the store via IOutboxStore.BeginOutboxTransaction.
// The Postgres adapter will implement the same IOutboxTransaction shape over an Npgsql
// connection so outbox rows commit in the same transaction as the handler's writes.