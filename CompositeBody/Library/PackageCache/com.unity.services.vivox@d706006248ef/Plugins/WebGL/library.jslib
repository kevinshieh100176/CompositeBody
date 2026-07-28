var libVivoxNative = {
    //Everything inside $Vivox can not be called for as a delegate from C#
    $Vivox: {
        ACCESS_TOKEN_DURATION: 90,
        LOG_RECORDS_LIMIT: 500,
        loginSession: null,
        channelSession: null,
        streamLocal: null,
        params: {},
        callbacks: {
            channelSessionCallbacks: null,
            loginSessionCallbacks: null,
        },
        logName: "[Vivox]",
        voiceMeter: {
            analyser: null,
            dataArray: null,
            rmsValueArray: null,
            rmsInterval: null,
            rmsIdx: 0,
            callback: null,
            addRMSValue: function (rmsval) {
                this.rmsValueArray[this.rmsIdx] = rmsval;

                this.rmsIdx += 1;
                if (this.rmsIdx >= this.rmsValueArray.length) {
                    this.rmsIdx = 0;
                }
            },
            getLevel: function () {
                if (this.rmsInterval == null) {
                    this.enable();
                }

                sum = 0;

                for (var i = 0; i < this.rmsValueArray.length; i++) {
                    sum += this.rmsValueArray[i];
                }

                return sum / this.rmsValueArray.length;
            },
            getRMS: function () {
                // Safety check in the case we don't have callbacks setup or mic access
                if (
                    Vivox != null &&
                    Vivox.voiceMeter != null &&
                    Vivox.voiceMeter.analyser != null &&
                    Vivox.voiceMeter.callback != null
                ) {
                    Vivox.voiceMeter.analyser.getFloatTimeDomainData(
                        Vivox.voiceMeter.dataArray
                    );

                    var sum = 0;
                    for (
                        var i = 0;
                        i < Vivox.voiceMeter.dataArray.length;
                        i++
                    ) {
                        sum +=
                            Vivox.voiceMeter.dataArray[i] *
                            Vivox.voiceMeter.dataArray[i];
                    }

                    Vivox.voiceMeter.addRMSValue(
                        Math.sqrt(sum / Vivox.voiceMeter.dataArray.length)
                    );

                    Vivox.dyncall(
                        "vd",
                        Vivox.voiceMeter.callback,
                        Vivox.voiceMeter.getLevel()
                    );
                }
            },
            disable: function () {
                if (Vivox.voiceMeter.rmsInterval != null) {
                    clearInterval(Vivox.voiceMeter.rmsInterval);
                    Vivox.voiceMeter.rmsInterval = null;
                }
            },
            enable: function () {
                if (Vivox.streamLocal === null)
                    // This prevents an exception in the webbrowser which triggers a popup - we should tie voice meter into if there is a valid stream
                    return;

                if (
                    Vivox.voiceMeter.rmsInterval != null ||
                    Vivox.voiceMeter.callback == null
                ) {
                    return;
                }

                Vivox.voiceMeter.rmsValueArray = new Array(5).fill(0);
                Vivox.voiceMeter.rmsIdx = 0;

                var audioCtx = new AudioContext();
                Vivox.voiceMeter.analyser = audioCtx.createAnalyser();
                Vivox.voiceMeter.analyser.fftSize = 2048;

                Vivox.voiceMeter.dataArray = new Float32Array(
                    Vivox.voiceMeter.analyser.fftSize
                );
                Vivox.voiceMeter.analyser.getFloatTimeDomainData(
                    Vivox.voiceMeter.dataArray
                );

                var source = audioCtx.createMediaStreamSource(
                    Vivox.streamLocal
                );
                source.connect(Vivox.voiceMeter.analyser);

                Vivox.voiceMeter.rmsInterval = setInterval(
                    Vivox.voiceMeter.getRMS,
                    100
                );
            },
        },
        initUserMedia: function () {
            var configGetUserMedia = {
                video: false,
                audio: true,
            };
            {
                var urlParams;
                if (typeof URLSearchParams === "function") {
                    urlParams = new URLSearchParams(location.hash.substr(1));
                }
                if (
                    urlParams &&
                    urlParams.has("echocancellation") &&
                    urlParams.get("echocancellation") == "false"
                ) {
                    configGetUserMedia.audio = {
                        echoCancellation: false,
                    };
                }
            }
            if (navigator.mediaDevices.getUserMedia) {
                navigator.mediaDevices
                    .getUserMedia(configGetUserMedia)
                    .then(Vivox.onGetUserMediaSuccess)
                    .catch(Vivox.onGetUserMediaError);
            } else {
                Vivox.onGetUserMediaError(
                    "The browser does not support getUserMedia() API"
                );
            }
        },
        getPtrFromString: function (str) {
            var buffer = _malloc(lengthBytesUTF8(str) + 1);
            stringToUTF8(str, buffer, str.length + 1);
            return buffer;
        },
        numberToString: function (n, minLength) {
            if (!minLength) minLength = 1;
            var s = "" + n;
            while (s.length < minLength) {
                s = "0" + s;
            }
            return s;
        },
        formatTime: function (dt) {
            return (
                Vivox.numberToString(dt.getHours(), 2) +
                ":" +
                Vivox.numberToString(dt.getMinutes(), 2)
            );
        },
        // Vivox specific function to ensure that we can call the correct function based on the editor version used by the customer
        // Use UTF8ToString if it is defined, otherwise use Pointer_stringify from older Editor versions
        pointer_stringify: function (ptr) {
            if (typeof UTF8ToString != "undefined" && UTF8ToString != null) {
                return UTF8ToString(ptr);
            } else {
                return Pointer_stringify(ptr);
            }
        },
        // Vivox specific function to ensure that we can call the correct function based on the editor version used by the customer
        // Use Runtime if it is defined, otherwise use Module which is in newer Editor versions
        dyncall: function (paramTypes, callback, args) {
            if (typeof Runtime != "undefined" && Runtime != null) {
                Runtime.dynCall(paramTypes, callback, [args]);
            } else {
                Module["dynCall_" + paramTypes](callback, args);
            }
        },
        callbackInteger: function (callback, isTrue) {
            if (callback !== null) {
                Vivox.dyncall("vi", callback, isTrue ? 1 : 0);
            }
        },
        callbackDouble: function (callback, doubleVal) {
            if (callback !== null) {
                Vivox.dyncall("vd", callback, doubleVal);
            }
        },
        simplifyParticipant: function (participant) {
            var channelParticipant = {
                account: participant.account,
                inAudio: participant.inAudio,
                inText: participant.inText,
                isSelf: participant.isSelf,
                localMute: participant.localMute,
                parentChannelSession: {
                    channel: participant.parentChannelSession.channel,
                    _accountJid: participant.parentChannelSession._accountJid,
                    _channelJid: participant.parentChannelSession._channelJid,
                },
                participantId: participant.participantId,
                role: participant.role,
            };
            return channelParticipant;
        },
        onGetUserMediaSuccess: function (stream) {
            Vivox.streamLocal = stream;
            if (Vivox.channelSession != null && Vivox.streamLocal != null) {
                Vivox.channelSession.streamLocal = Vivox.streamLocal;
            }
            Vivox.voiceMeter.enable();
        },
        onGetUserMediaError: function (error) {
            Vivox.streamLocal = null;
            var errorString = "getUserMedia() failure";
            if (error) {
                errorString += ": ";
                errorString += error.toString();
            }
            console.log(Vivox.logName, errorString);
        },
        onRemoteStream: function (channelSession, stream, idRTCPC) {
            // Vivox5.ChannelSession.onRemoteStream callback handler -- called when a new audio stream is added to the ChannelSession.
            // NB: THIS IS MOST IMPORTANT EVENT OF THE ChannelSession that the web application MUST HANDLE!
            // The web application MUST create new <audio/> HTML element on the web page and connect it with the 'stream' parameter passed from the SDK.
            // NB: MULTIPLE onRemoteStream events can happen in one ChannelSession! The web application MUST create separate <audio/> element for each event!
            var elsContainerAudio =
                document.getElementsByClassName("vxaudiolist");
            if (!elsContainerAudio || elsContainerAudio.length < 1) {
                var ul = document.createElement("ul");
                ul.classList.add("vxaudiolist");
                ul.style.display = "none";
                document.body.appendChild(ul);
                elsContainerAudio =
                    document.getElementsByClassName("vxaudiolist");
                if (!elsContainerAudio || elsContainerAudio.length < 1) {
                    console.log(Vivox.logName,
                    "Fatal error: vxaudiolist not found on the page for the session " +
                            channelSession.channel.name
                    );
                    return;
                }
            }
            var elContainerAudio = elsContainerAudio[0]; // Per-session audio container

            var elAudio = elContainerAudio.querySelector(
                'audio[data-rtcpcid="' +
                    idRTCPC +
                    '"][data-streamid="' +
                    stream.id +
                    '"][data-sessionid="' +
                    channelSession.channel.name +
                    '"]'
            );
            if (elAudio) {
                // should not happen
                return;
            }
            elAudio = document.createElement("audio");
            elAudio.setAttribute("data-rtcpcid", idRTCPC);
            elAudio.setAttribute("data-streamid", stream.id);
            elAudio.setAttribute("data-sessionid", channelSession.channel.name);
            elAudio.setAttribute("autoplay", 1);
            elAudio.controls = false;
            elAudio.srcObject = stream;

            var elAudioItem = document.createElement("li");
            elAudioItem.className = "vxaudioitem";
            elContainerAudio.appendChild(elAudioItem);
            elAudioItem.appendChild(elAudio);

            console.log(
                Vivox.logName,
                "Created html audio element for stream " +
                    stream.id +
                    " in RTPCP " +
                    idRTCPC +
                    " of ChannelSession " +
                    channelSession.channel.name
            );
        },
        onConnected: function (_loginSession) {
            if (
                Vivox.callbacks.loginSessionCallbacks != null &&
                Vivox.callbacks.loginSessionCallbacks.onConnected != null
            )
                Vivox.callbackInteger(
                    Vivox.callbacks.loginSessionCallbacks.onConnected,
                    true
                );
            Vivox.loginSession.presence = new Vivox5.Presence("AVAILABLE", null);
        },
        onDisconnected: function (_loginSession) {
            if (
                Vivox.callbacks.loginSessionCallbacks != null &&
                Vivox.callbacks.loginSessionCallbacks.onDisconnected != null
            ) {
                Vivox.callbackInteger(
                    Vivox.callbacks.loginSessionCallbacks.onDisconnected,
                    false
                );
            }
            if (Vivox.channelSession == null) {
                Vivox.callbacks.channelSessionCallbacks = null;
            }
            if (Vivox.loginSession == null) {
                Vivox.callbacks.loginSessionCallback = null;
            }
        },
        onConnFailure: function (_loginSession) {
            if (
                Vivox.callbacks.loginSessionCallbacks != null &&
                Vivox.callbacks.loginSessionCallbacks.onDisconnected != null
            )
                Vivox.callbackInteger(
                    Vivox.callbacks.loginSessionCallbacks.onDisconnected,
                    false
                );

            if (Vivox.channelSession == null) {
                Vivox.callbacks.channelSessionCallbacks = null;
            }
            if (Vivox.loginSession == null) {
                Vivox.callbacks.loginSessionCallback = null;
            }
        },
        isConnected: function () {
            return Vivox.loginSession && Vivox.loginSession.isConnected;
        },
        onSessionEstablished: function (channelSession) {
            if (
                Vivox.callbacks.channelSessionCallbacks != null &&
                Vivox.callbacks.channelSessionCallbacks.onSessionEstablished !=
                    null
            )
                Vivox.callbackInteger(
                    Vivox.callbacks.channelSessionCallbacks
                        .onSessionEstablished,
                    true
                );
        },
        onSessionTerminated: function (channelSession) {
            if (
                Vivox.callbacks.channelSessionCallbacks != null &&
                Vivox.callbacks.channelSessionCallbacks.onSessionTerminated !=
                    null
            ) {
                Vivox.callbackInteger(
                    Vivox.callbacks.channelSessionCallbacks.onSessionTerminated,
                    false
                );
            }
            if (Vivox.channelSession == null) {
                Vivox.callbacks.channelSessionCallbacks = null;
            }
        },
        onChannelSessionRemoved: function (_loginSession, channelSession) {
            if (
                Vivox.callbacks.channelSessionCallbacks != null &&
                Vivox.callbacks.channelSessionCallbacks
                    .onChannelSessionRemoved != null
            ) {
                Vivox.callbackInteger(
                    Vivox.callbacks.channelSessionCallbacks
                        .onChannelSessionRemoved,
                    false
                );
            }
            if (Vivox.channelSession == null) {
                Vivox.callbacks.channelSessionCallbacks = null;
            }
        },
        onChannelTextMessage: function (channelSession, message) {
            var formattedDate = Vivox.formatTime(
                message.receivedTime
                    ? new Date(message.receivedTime)
                    : new Date()
            );
            var responseObject = {
                Language: message.language,
                Message: message.message,
                ReceivedTime: formattedDate,
                Sender: {
                    Issuer: message.sender.issuer,
                    Name: message.sender.name,
                    EnvironmentId: message.sender.environmentId,
                    Domain: message.sender.domain,
                },
                SenderParticipant: {
                    InAudio: message.senderParticipant.inAudio,
                    InText: message.senderParticipant.inText,
                    IsSelf: message.senderParticipant.isSelf,
                    LocalMute: message.senderParticipant.localMute,
                },
            };

            if (
                message.senderParticipant &&
                message.message &&
                Vivox.callbacks.channelSessionCallbacks != null &&
                Vivox.callbacks.channelSessionCallbacks.onChannelTextMessage
            ) {
                // && !message.receivedTime) {

                var buffer = Vivox.getPtrFromString(
                    JSON.stringify(responseObject)
                );
                Vivox.dyncall("vi", Vivox.callbacks.channelSessionCallbacks.onChannelTextMessage,buffer);
                _free(buffer);
            }
        },
        onIncomingDirectTextMessage: function (loginSession, message) {
            var formattedDate = Vivox.formatTime(
                message.receivedTime
                    ? new Date(message.receivedTime)
                    : new Date()
            );
            var responseObject = {
                Language: message.language,
                Message: message.message,
                ReceivedTime: formattedDate,
                Sender: {
                    Issuer: message.sender.issuer,
                    Name: message.sender.name,
                    EnvironmentId: message.sender.environmentId,
                    Domain: message.sender.domain,
                },
            };

            if (
                message.message &&
                Vivox.callbacks.loginSessionCallbacks != null &&
                Vivox.callbacks.loginSessionCallbacks.onIncomingDirectTextMessage
            ) {
                // && !message.receivedTime) {

                var buffer = Vivox.getPtrFromString(
                    JSON.stringify(responseObject)
                );
                Vivox.dyncall("vi", Vivox.callbacks.loginSessionCallbacks.onIncomingDirectTextMessage, buffer);
                _free(buffer);
            }
        },
        onParticipantAdded: function (channelSession, participant) {
            var channelParticipant = Vivox.simplifyParticipant(participant);

            if (
                channelParticipant &&
                Vivox.callbacks.channelSessionCallbacks != null &&
                Vivox.callbacks.channelSessionCallbacks.onParticipantAdded
            ) {
                // && !message.receivedTime) {
                var buffer = Vivox.getPtrFromString(
                    JSON.stringify(channelParticipant)
                );
                Vivox.dyncall(
                    "vi",
                    Vivox.callbacks.channelSessionCallbacks.onParticipantAdded,
                    buffer
                );
                _free(buffer);
            }
        },
        onParticipantUpdated: function (channelSession, participant) {
            var channelParticipant = Vivox.simplifyParticipant(participant);

            if (
                channelParticipant &&
                Vivox.callbacks.channelSessionCallbacks != null &&
                Vivox.callbacks.channelSessionCallbacks.onParticipantUpdated
            ) {
                // && !message.receivedTime) {
                var buffer = Vivox.getPtrFromString(
                    JSON.stringify(channelParticipant)
                );
                Vivox.dyncall(
                    "vi",
                    Vivox.callbacks.channelSessionCallbacks
                        .onParticipantUpdated,
                    buffer
                );
                _free(buffer);
            }
        },
        onParticipantRemoved: function (channelSession, participant) {
            var channelParticipant = Vivox.simplifyParticipant(participant);

            if (
                channelParticipant &&
                Vivox.callbacks.channelSessionCallbacks != null &&
                Vivox.callbacks.channelSessionCallbacks.onParticipantRemoved
            ) {
                // && !message.receivedTime) {
                var buffer = Vivox.getPtrFromString(
                    JSON.stringify(channelParticipant)
                );
                Vivox.dyncall(
                    "vi",
                    Vivox.callbacks.channelSessionCallbacks
                        .onParticipantRemoved,
                    buffer
                );
                _free(buffer);
            }
        },
    },
    //Everything outside of $Vivox and inside of libVivoxNative can be called for as a delegate from C#
    vx_initialize: function () {
        if (Vivox5.Client.isInitialized) {
            return;
        }

        if (!Vivox5.Client.initialize()) {
            console.log(Vivox.logName, "Failed to initialize Vivox5 Client");
        }
        Vivox.initUserMedia();
    },
    vx_uninitialize: function () {
        if (!Vivox5.Client.uninitialize()) {
            console.log(Vivox.logName, "Failed to uninitialize Vivox5 Client");
        }
    },
    vx_is_initialized: function () {
        return Vivox5.Client.isInitialized ? 1 : 0;
    },

    vx_debugGenerateToken: function (
        issuer,
        expiration,
        vxa,
        subject,
        from_uri,
        to_uri,
        secret
    ) {
        var mapping = {
            issuer: Vivox.pointer_stringify(issuer),
            expiration: expiration,
            vxa: Vivox.pointer_stringify(vxa),
            subject: Vivox.pointer_stringify(subject),
            from_uri: Vivox.pointer_stringify(from_uri),
            to_uri: Vivox.pointer_stringify(to_uri),
            secret: Vivox.pointer_stringify(secret),
        };

        var token = Vivox5.Token.debugGenerateAccessToken(
            mapping.issuer,
            mapping.expiration,
            mapping.vxa,
            mapping.subject,
            mapping.from_uri,
            mapping.to_uri,
            mapping.secret
        );
        var buffer = Vivox.getPtrFromString(token);
        return buffer;
    },
    vx_setPresenceStatus: function (newStatus) {
        Vivox.loginSession.presence = new Vivox5.Presence(
            newStatus,
            null
        );
    },
    vx_isConnected: function () {
        return Vivox.isConnected;
    },
    vx_getLoginToken: function (tokenSigningKey, tokenExpirationDuration) {
        var token = Vivox.loginSession.getLoginToken(
            tokenSigningKey,
            tokenExpirationDuration
        );
        var buffer = Vivox.getPtrFromString(token);
        return buffer;
    },
    vx_setupLoginSessionCallbacks: function (
        onLoginStateChange,
        onDirectedTextMessage
    ) {
        Vivox.callbacks.loginSessionCallbacks = {
            onConnected: onLoginStateChange,
            onDisconnected: onLoginStateChange,
            onConnFailure: onLoginStateChange,
            onIncomingDirectTextMessage: onDirectedTextMessage,
        };
    },
    vx_initiateLoginSession: function (
        userId,
        realm,
        issuer,
        environmentId,
        incomingSub,
        isSecurePage,
        loginAccessToken
    ) {
        var initialized = 0;
        if (Vivox.loginSession) {
            return 1;
        }
        Vivox.params.realm = Vivox.pointer_stringify(realm);
        Vivox.params.username = Vivox.pointer_stringify(userId);
        Vivox.params.sessmode = "auto";
        Vivox.params.issuer = Vivox.pointer_stringify(issuer);
        Vivox.params.environmentId = Vivox.pointer_stringify(environmentId);

        var isSecurePageUsed = true;

        var accountId = new Vivox5.AccountId(
            Vivox.params.issuer,
            Vivox.params.username,
            Vivox.params.realm,
            Vivox.params.environmentId
        );

        Vivox.loginSession = Vivox5.Client.getLoginSession(
            accountId,
            isSecurePageUsed,
            undefined
        );
        Vivox.loginSession.onConnected = Vivox.onConnected;
        Vivox.loginSession.onDisconnected = Vivox.onDisconnected;
        Vivox.loginSession.onConnFailure = Vivox.onDisconnected;
        Vivox.loginSession.onDirectedTextMessage = Vivox.onIncomingDirectTextMessage;

        loginAccessToken = Vivox.pointer_stringify(loginAccessToken);

        var rc = Vivox.loginSession.login(loginAccessToken, incomingSub);
        if (!rc) {
            console.log(Vivox.logName, "LoginSession failed, see details in the log");
            Vivox.loginSession = undefined;
            initialized = 1;
        }

        Vivox.voiceMeter.enable();
        return initialized;
    },
    vx_terminateLoginSession: function () {
        var status = 0;
        if (Vivox.isConnected()) {
            Vivox.loginSession.presence = new Vivox5.Presence("UNAVAILABLE", null);
            Vivox.loginSession.logout();
        }
        Vivox.voiceMeter.disable();
        Vivox.loginSession = undefined;
        return status;
    },
    vx_setupLoginChannelSessionCallbacks: function (
        onSessionEstablished,
        onChannelSessionRemoved,
        onRemoteStream,
        onParticipantAdded,
        onParticipantUpdated,
        onParticipantRemoved,
        onChannelTextMessage,
        onSessionTerminated
    ) {
        try {
            Vivox.callbacks.channelSessionCallbacks = {
                onSessionEstablished: onSessionEstablished,
                onChannelSessionRemoved: onChannelSessionRemoved,
                onRemoteStream: onRemoteStream,
                onParticipantAdded: onParticipantAdded,
                onParticipantUpdated: onParticipantUpdated,
                onParticipantRemoved: onParticipantRemoved,
                onSessionTerminated: onSessionTerminated,
                onChannelTextMessage: onChannelTextMessage,
            };
        } catch (error) {
            console.error(Vivox.logName, error);
            // Expected output: ReferenceError: nonExistentFunction is not defined
            // (Note: the exact output may be browser-dependent)
        }
    },
    vx_createChannelSession: function (
        channelName,
        channelType,
        isAudioInt,
        isTextInt,
        token
    ) {
        try {
            var status = 0;
            var environmentId = null;
            var isAudio = isAudioInt == 0 ? false : true;
            var isText = isTextInt == 0 ? false : true;
            Vivox.params.channelName = Vivox.pointer_stringify(channelName);
            if (!Vivox.isConnected()) {
                console.log(Vivox.logName,
                    "Cannot start ChannelSession: Please make sure you are logged into Vivox first."
                );
                status = 1;
                return status;
            }
            if (isAudio && !Vivox.streamLocal) {
                //Check for local permissions to the audio stream
                Vivox.initUserMedia();
                if (!Vivox.streamLocal) {
                    console.log(Vivox.logName,
                        "Cannot start audio for ChannelSession: Please allow the page to access the microphone when prior to joining channels."
                    );
                }
            }
            var channelTypeWeb =
                Vivox.pointer_stringify(channelType) == "g" || channelType == 0
                    ? "g"
                    : "e";
            if (
                Vivox.params.environmentId != null &&
                Vivox.params.environmentId !== ""
            ) {
                environmentId = Vivox.params.environmentId;
            }
            var channelId = new Vivox5.ChannelId(
                Vivox.params.issuer,
                Vivox.params.channelName,
                Vivox.params.realm,
                channelTypeWeb,
                environmentId
            );
            var channelSession =
                Vivox.loginSession.getChannelSession(channelId);

            if (!channelSession) {
                console.log(Vivox.logName, "getChannelSession failed, see details in the log");
                Vivox.callbackInteger(onChannelCallback, false);
                status = 1;
                return status;
            }
            channelSession.onSessionEstablished = Vivox.onSessionEstablished;
            channelSession.onChannelSessionRemoved =
                Vivox.onChannelSessionRemoved;
            channelSession.onRemoteStream = Vivox.onRemoteStream;
            channelSession.onParticipantAdded = Vivox.onParticipantAdded;
            channelSession.onParticipantUpdated = Vivox.onParticipantUpdated;
            channelSession.onParticipantRemoved = Vivox.onParticipantRemoved;
            channelSession.onChannelTextMessage = Vivox.onChannelTextMessage;
            channelSession.onSessionTerminated = Vivox.onSessionTerminated;
            if (isAudio && Vivox.streamLocal) {
                channelSession.streamLocal = Vivox.streamLocal;
            } else {
                // Because the channel is Text only, or streamLocal is null (meaning we don't have permission for the microphone) we cant join voice
                isAudio = false;

            }
            Vivox.channelSession = channelSession;

            if (
                !channelSession.connect(
                    isAudio,
                    isText,
                    Vivox.pointer_stringify(token)
                )
            ) {
                Vivox.callbackInteger(onChannelCallback, false);
                status = 1;
                return status;
            }
        } catch (error) {
            console.error(Vivox.logName, error);
            status = 1;
            // Expected output: ReferenceError: nonExistentFunction is not defined
            // (Note: the exact output may be browser-dependent)
        }
        return status;
    },
    vx_muteForMe: function (isMuted, participantId) {
        if (Vivox.channelSession) {
            var partId = Vivox.pointer_stringify(participantId);

            // Get the list of participants as an array from the web sdk
            var parts = Object.keys(Vivox.channelSession.participants);

            // Iterate over the keys until we find the one that matches the participantId we want to mute
            for (var i = 0; i < parts.length; i++) {
                if (parts[i].startsWith(partId)) {
                    Vivox.channelSession.muteForMe(
                        !!isMuted,
                        Vivox.channelSession.participants[parts[i]]
                    );
                }
            }
        }
    },
    vx_setLocalCapture: function (isTransmitting) {
        if (Vivox.channelSession) {
            Vivox.channelSession.muteLocalCapture(!!isTransmitting);
        }
    },
    vx_setLocalRender: function (isMuted) {
        if (Vivox.channelSession) {
            var audioEls = document.getElementsByClassName("vxaudioitem");

            for (var i = 0; i < audioEls.length; i++) {
                audioEls[i].firstChild.srcObject.getAudioTracks()[0].enabled =
                    !!isMuted;
            }
        }
    },
    vx_terminateChannelSession: function () {
        if (!Vivox.isConnected() || !Vivox.channelSession) {
            return;
        }
        Vivox.channelSession.disconnect();
        Vivox.channelSession = null;
    },
    vx_sendDirectedTextMessage: function (
        ptr_destination_account,
        ptr_message,
        ptr_language,
        ptr_applicationStanzaNamespace,
        ptr_application_stanza_body
    ) {
        var mapping = {
            account: Vivox.pointer_stringify(ptr_destination_account),
            message: Vivox.pointer_stringify(ptr_message),
            language: Vivox.pointer_stringify(ptr_language),
            applicationStanzaNamespace: Vivox.pointer_stringify(
                ptr_applicationStanzaNamespace
            ),
            application_stanza_body: Vivox.pointer_stringify(
                ptr_application_stanza_body
            ),
        };

        var accountId = new Vivox5.AccountId(
            Vivox.params.issuer,
            mapping.account,
            Vivox.params.realm,
            Vivox.params.environmentId
        );

        Vivox.loginSession.sendDirectedMessage(
            accountId,
            mapping.message,
            undefined,
            mapping.language
        );
    },
    vx_sendChannelTextMessage: function (
        ptr_message,
        ptr_language,
        ptr_applicationStanzaNamespace,
        ptr_application_stanza_body
    ) {
        var mapping = {
            message: Vivox.pointer_stringify(ptr_message),
            language: Vivox.pointer_stringify(ptr_language),
            applicationStanzaNamespace: Vivox.pointer_stringify(
                ptr_applicationStanzaNamespace
            ),
            application_stanza_body: Vivox.pointer_stringify(
                ptr_application_stanza_body
            ),
        };

        Vivox.channelSession.sendText(
            mapping.message,
            undefined,
            mapping.language,
            mapping.applicationStanzaNamespace,
            mapping.applicationStanzaBody
        );
    },
    vx_disableVoiceMeter: function () {
        Vivox.voiceMeter.disable();
    },
    vx_enableVoiceMeter: function (callbackPtr) {
        Vivox.voiceMeter.callback = callbackPtr;
    },
    vx_getVoiceLevel: function () {
        var voiceLevel = Vivox.voiceMeter.getLevel().toString();
        var bufferSize = lengthBytesUTF8(voiceLevel) + 1;
        var buffer = _malloc(bufferSize);
        stringToUTF8(voiceLevel, buffer, bufferSize);
        return buffer;
    },
    getURLFromPage: function () {
        var returnStr = window.top.location.href;
        var bufferSize = lengthBytesUTF8(returnStr) + 1;
        var buffer = _malloc(bufferSize);
        stringToUTF8(returnStr, buffer, bufferSize);
        return buffer;
    },
    getQueryParam: function (paramId) {
        var urlParams = new URLSearchParams(location.search);
        var param = urlParams.get(Vivox.pointer_stringify(paramId));
        if (param == null) {
            param = "";
        }
        var bufferSize = lengthBytesUTF8(param) + 1;
        var buffer = _malloc(bufferSize);
        stringToUTF8(param, buffer, bufferSize);
        return buffer;
    },
};
autoAddDeps(libVivoxNative, "$Vivox");
mergeInto(LibraryManager.library, libVivoxNative);
