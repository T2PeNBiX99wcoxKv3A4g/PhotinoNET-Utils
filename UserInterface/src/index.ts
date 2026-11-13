export {Native} from "./Native";
export type {NativeMessage} from "./Native";

export {
    createTranslator,
    LocaleContext,
    LocalizationProvider,
    loadStrings,
    TranslationContext,
    useLocale,
    useTranslation,
} from "./Localization";
export type {I18nPayload, Translate} from "./Localization";

export {ResizeHandles, useWindowControls, useWindowCorners, useWindowDrag} from "./Window";
export type {WindowControls} from "./Window";

export {
    type AppInfo,
    type StartupStatus,
    type UpdateAck,
    type UpdateChangelog,
    type UpdateCheck,
    type UpdatePhase,
    type UpdateProgress,
    cancelUpdateDownload,
    checkForUpdates,
    getAppInfo,
    getUpdateStatus,
    installUpdate,
    onUpdateChangelog,
    onUpdateProgress,
    openLogsFolder,
    openReleasesPage,
    openUrl,
    setStartup,
    startUpdateDownload,
    startupStatus,
} from "./System";
