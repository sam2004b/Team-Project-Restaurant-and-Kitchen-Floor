
import { useEffect, useRef } from "react";

import {
    HubConnectionBuilder,
    HubConnectionState,
    LogLevel,
} from "@microsoft/signalr";

import { apiBaseUrl } from "./api";

export function useMenuUpdates(
    onMenuChanged: () => void,
): void {
    // Keep the latest callback without reconnecting
    // every time the React component renders.
    const callbackRef = useRef(onMenuChanged);
    callbackRef.current = onMenuChanged;

    useEffect(() => {
        let disposed = false;
        let retryTimer: ReturnType<typeof setTimeout> | undefined;

        const connection = new HubConnectionBuilder()
            .withUrl(`${apiBaseUrl}/hubs/menu`, {
                withCredentials: false,
            })
            .withAutomaticReconnect([
                0,
                2000,
                5000,
                10000,
                30000,
            ])
            .configureLogging(LogLevel.Warning)
            .build();

        const refreshMenu = () => {
            if (!disposed) {
                callbackRef.current();
            }
        };

        const connect = async () => {
            if (
                disposed ||
                connection.state !== HubConnectionState.Disconnected
            ) {
                return;
            }

            try {
                await connection.start();

                if (disposed) {
                    await connection.stop();
                    return;
                }

                // Fetch current data after connecting.
                // This also covers changes made while offline.
                refreshMenu();
            } catch (error) {
                if (!disposed) {
                    console.warn(
                        "Live menu connection failed. Retrying...",
                        error,
                    );

                    retryTimer = setTimeout(() => {
                        void connect();
                    }, 5000);
                }
            }
        };

        connection.on("MenuChanged", refreshMenu);

        connection.onreconnected(() => {
            refreshMenu();
        });

        connection.onclose(() => {
            if (!disposed) {
                retryTimer = setTimeout(() => {
                    void connect();
                }, 5000);
            }
        });

        void connect();

        return () => {
            disposed = true;

            if (retryTimer !== undefined) {
                clearTimeout(retryTimer);
            }

            connection.off("MenuChanged", refreshMenu);

            void connection.stop();
        };
    }, []);
}
