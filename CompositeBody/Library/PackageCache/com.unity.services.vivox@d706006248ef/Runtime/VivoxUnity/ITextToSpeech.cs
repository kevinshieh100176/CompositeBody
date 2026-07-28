using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Unity.Services.Vivox
{
    /// <summary>The arguments for ITTSMessageQueue event notifications.</summary>
    internal sealed class ITTSMessageQueueEventArgs : EventArgs
    {
        public ITTSMessageQueueEventArgs(TTSMessage message) { Message = message; }

        /// <summary>The text-to-speech (TTS) message.</summary>
        public TTSMessage Message { get; }
    }

    internal interface ITTSMessageQueue : IEnumerable<TTSMessage>
    {
        /// <summary>Raised when a TTSMessage is added to the text-to-speech subsystem.</summary>
        event EventHandler<ITTSMessageQueueEventArgs> AfterMessageAdded;

        /// <summary>Raised when a TTSMessage is removed from the text-to-speech subsystem.</summary>
        /// <remarks>This can result from either cancellation or playback completion.</remarks>
        event EventHandler<ITTSMessageQueueEventArgs> BeforeMessageRemoved;

        /// <summary>Raised when playback begins for a TTSMessage in the collection.</summary>
        event EventHandler<ITTSMessageQueueEventArgs> AfterMessageUpdated;

        /// <summary>
        /// Remove all objects from the collection and cancel them.
        /// </summary>
        /// <seealso cref="ITextToSpeech.CancelAll" />
        void Clear();

        /// <summary>
        /// Determine whether a TTSMessage is in the collection.
        /// </summary>
        /// <param name="message">The TTSMessage to locate in the collection.</param>
        bool Contains(TTSMessage message);

        /// <summary>
        /// Get the number of elements contained in the collection.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Remove and return the oldest TTSMessage in the collection. This cancels the message.
        /// </summary>
        /// <seealso cref="ITextToSpeech.CancelMessage" />
        TTSMessage Dequeue();

        /// <summary>
        /// Add a message and speak it as the user that the collection belongs to.
        /// </summary>
        /// <param name="message">The TTSMessage to add and speak.</param>
        /// <seealso cref="ITextToSpeech.Speak" />
        void Enqueue(TTSMessage message);

        /// <summary>
        /// Return the oldest TTSMessage in the collection without removing it.
        /// </summary>
        TTSMessage Peek();

        /// <summary>
        /// Remove a specific message from the collection. This cancels the message.
        /// </summary>
        /// <param name="message">The TTSMessage to remove and cancel.</param>
        /// <seealso cref="ITextToSpeech.CancelMessage" />
        bool Remove(TTSMessage message);
    }

    /// <summary>
    /// An interface for events and methods related to text-to-speech.
    /// </summary>
    internal interface ITextToSpeech : INotifyPropertyChanged
    {
        /// <summary>
        /// All voices available to the text-to-speech subsystem for speech synthesis.
        /// </summary>
        ReadOnlyCollection<ITTSVoice> AvailableVoices { get; }

        /// <summary>
        /// The voice used by text-to-speech methods called from this ILoginSession.
        /// </summary>
        /// <remarks>
        /// If this is not set, then the SDK default voice is used. You can obtain valid ITTSVoices from AvailableVoices.
        /// When setting this, if the new voice is not available (for example, when loaded from saved settings after updating),
        /// then ObjectNotFoundException is raised.
        /// </remarks>
        ITTSVoice CurrentVoice { get; set; }

        /// <summary>
        /// Inject a new text-to-speech (TTS) message into the TTS subsystem.
        /// </summary>
        /// <param name="message">A TTSMessage that contains the text to be converted into speech and the message type for TTS injection.</param>
        /// <remarks>
        /// The Voice and State properties of the message are set by this function.
        /// For information on how the ITTSVoice that is used for speech synthesis is selected, see CurrentVoice.
        /// Synthesized speech sent to remote destinations plays in connected channel sessions
        /// according to the transmission policy (the same sessions that basic voice transmits to).
        /// </remarks>
        void Speak(TTSMessage message);

        /// <summary>
        /// Cancel a single currently playing or enqueued text-to-speech message.
        /// </summary>
        /// <param name="message">The TTSMessage to cancel.</param>
        /// <remarks>
        /// For message types with queues, canceling an ongoing message automatically triggers the playback of
        /// the next message. Canceling an enqueued message shifts all later messages up one place in the queue.
        /// </remarks>
        void CancelMessage(TTSMessage message);

        /// <summary>
        /// Cancel all text-to-speech messages of a particular type (ongoing and enqueued).
        /// </summary>
        /// <param name="messageType">The type of TTS messages to clear.</param>
        /// <remarks>
        /// The TextToSpeechMessageType QueuedRemoteTransmission and QueuedRemoteTransmissionWithLocalPlayback
        /// share a queue, but are not the same mesesage type. Canceling all messages of one of these types
        /// automatically triggers the playback of the next message from the other in the shared queue.
        /// </remarks>
        void CancelDestination(TextToSpeechMessageType messageType);

        /// <summary>
        /// Cancel all text-to-speech messages (ongoing and enqueued) of all message types.
        /// </summary>
        void CancelAll();

        /// <summary>
        /// Contains all text-to-speech (TTS) messages playing or waiting to be played of all message types.
        /// </summary>
        /// <remarks>
        /// Use the ITTSMessageQueue events to get notifications of when messages are spoken or canceled, or when playback starts or ends.
        /// Methods to Enqueue(), Dequeue(), Remove(), or Clear() items directly from this collection result in the same
        /// behavior as using other class methods to Speak() or Cancel*() TTS messages in the text-to-speech subsystem.
        /// </remarks>
        ITTSMessageQueue Messages { get; }

        /// <summary>
        /// Retrieve ongoing or enqueued TTSMessages of thet specified message type.
        /// </summary>
        /// <param name="messageType">The TextToSpeechMessageType to retrieve messages for.</param>
        /// <returns>A queue containing the messages for a single message type.</returns>
        /// <remarks>
        /// Queued messager types return their ITTSMessageQueue in queue order, and others in the order that speech was injected.
        /// </remarks>
        ReadOnlyCollection<TTSMessage> GetMessagesFromDestination(TextToSpeechMessageType messageType);
    }
}
