using Cabbie.Domain.Envelope.Validators;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Domain.Sagas;
using Cabbie.Domain.Sagas.ValueObjects;
using FluentValidation.TestHelper;

namespace Cabbie.Tests.Domain.Envelope;

public sealed class EnvelopeValidatorTests
{
    private readonly EnvelopeValidator _validator = new();

    [Fact]
    public void valid_envelope_passes()
    {
        var result = _validator.TestValidate(EnvelopeFactory.Create());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void envelope_carries_message_type_and_saga_id_without_owning_them()
    {
        var message = new PlaceOrder(42);
        var envelope = EnvelopeFactory.Create(
            message: new Message(message),
            messageType: Cabbie.Domain.Messaging.MessageTypeNaming.For(typeof(PlaceOrder)),
            sagaId: SagaIdentityNaming.For(message, typeof(OrderSaga)));

        Assert.Same(message, envelope.Message.Value);
        Assert.Equal(typeof(PlaceOrder).FullName, envelope.MessageType.Value);
        Assert.Equal("42", envelope.SagaId.Value);
    }

    [Fact]
    public void empty_saga_id_is_allowed()
    {
        var result = _validator.TestValidate(EnvelopeFactory.Create(sagaId: new SagaId("")));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void whitespace_saga_id_is_rejected()
    {
        var result = _validator.TestValidate(EnvelopeFactory.Create(sagaId: new SagaId("   ")));

        result.ShouldHaveValidationErrorFor(envelope => envelope.SagaId.Value);
    }

    [Fact]
    public void deliver_by_equal_to_sent_at_passes()
    {
        var sentAt = DateTimeOffset.UtcNow;

        var result = _validator.TestValidate(EnvelopeFactory.Create(sentAt: sentAt, deliverBy: sentAt));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void deliver_by_before_sent_at_is_rejected()
    {
        var sentAt = DateTimeOffset.UtcNow;

        var result = _validator.Validate(
            EnvelopeFactory.Create(sentAt: sentAt, deliverBy: sentAt.AddMinutes(-1)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("DeliverBy", StringComparison.Ordinal));
    }

    [Fact]
    public void content_type_without_data_is_rejected()
    {
        var result = _validator.Validate(
            EnvelopeFactory.Create(contentType: "application/json"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("EnvelopeData", StringComparison.Ordinal));
    }

    [Fact]
    public void data_without_content_type_is_rejected()
    {
        var result = _validator.Validate(
            EnvelopeFactory.Create(data: [1, 2, 3]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("ContentType", StringComparison.Ordinal));
    }

    [Fact]
    public void content_type_and_data_together_pass()
    {
        var result = _validator.TestValidate(
            EnvelopeFactory.Create(contentType: "application/json", data: [1, 2, 3]));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void first_attempt_is_one()
    {
        var result = _validator.TestValidate(EnvelopeFactory.Create(attempts: 0));

        result.ShouldHaveValidationErrorFor(envelope => envelope.Attempts.Value)
            .WithErrorMessage("Attempts must be at least 1. The first execution is attempt 1.");
    }

    [Fact]
    public void retries_reuse_the_same_envelope_id()
    {
        var first = EnvelopeFactory.Create(attempts: 1);
        var retry = first with { Attempts = new Attempts(2) };

        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(2, retry.Attempts.Value);
        Assert.True(_validator.Validate(retry).IsValid);
    }

    [Fact]
    public void empty_envelope_id_is_rejected()
    {
        var result = _validator.TestValidate(EnvelopeFactory.Create(id: new EnvelopeId(Guid.Empty)));

        result.ShouldHaveValidationErrorFor(envelope => envelope.Id.Value);
    }

    [Fact]
    public void destination_scheme_is_not_restricted_because_registered_transports_decide()
    {
        var result = _validator.TestValidate(
            EnvelopeFactory.Create(destination: new Destination(new Uri("sqs://orders/"))));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void destination_must_be_an_absolute_uri()
    {
        var result = _validator.TestValidate(
            EnvelopeFactory.Create(destination: new Destination(new Uri("orders", UriKind.Relative))));

        result.ShouldHaveValidationErrorFor(envelope => envelope.Destination.Value);
    }

    [Fact]
    public void empty_message_type_is_rejected()
    {
        var result = _validator.TestValidate(
            EnvelopeFactory.Create(messageType: new MessageType("")));

        result.ShouldHaveValidationErrorFor(envelope => envelope.MessageType.Value);
    }

    [Fact]
    public void empty_header_keys_are_rejected()
    {
        var result = _validator.TestValidate(
            EnvelopeFactory.Create(headers: new Headers(new Dictionary<string, string> { [""] = "value" })));

        result.ShouldHaveValidationErrorFor("Headers.Value.Keys[0]");
    }
}
