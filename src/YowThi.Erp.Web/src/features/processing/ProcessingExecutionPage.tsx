import { useMutation, useQuery } from '@tanstack/react-query';
import {
  type FormEvent,
  useDeferredValue,
  useMemo,
  useRef,
  useState,
} from 'react';

import { type OperationalLocale, useOperationalLocale } from '../../app/i18n/locale';
import {
  ApiProblemError,
  confirmProcessingExecution,
  type ConfirmProcessingExecutionRequest,
  type ProcessingSourceKind,
} from './confirmProcessingExecution';
import {
  getProcessingModuleOutputOptions,
  listProcessingBatchOptions,
  listProcessingEmployeeOptions,
  listProcessingInputStorageLocationOptions,
  listProcessingModuleOptions,
  listProcessingStorageLocationOptions,
  listProcessingSupplierOptions,
  type ProcessingBatchOption,
  type ProcessingEmployeeOption,
  type ProcessingModuleOption,
  type ProcessingModuleOutputOption,
  type ProcessingStorageLocationOption,
  type ProcessingSupplierOption,
} from './processingExecutionOptions';

interface Submission {
  request: ConfirmProcessingExecutionRequest;
  idempotencyKey: string;
  locale: OperationalLocale;
}

interface SubmissionIdentity {
  fingerprint: string;
  idempotencyKey: string;
}

interface OutputDraft {
  observedScaleReading: string;
  actualContainerCount: string;
  completedQuantity: string;
  outputStorageLocationId: string;
  outputStorageLocation: ProcessingStorageLocationOption | null;
}

const emptyOutputDraft = (): OutputDraft => ({
  observedScaleReading: '',
  actualContainerCount: '',
  completedQuantity: '',
  outputStorageLocationId: '',
  outputStorageLocation: null,
});

const copy = {
  'zh-TW': {
    eyebrow: 'P6 · 加工執行',
    title: '確認加工執行',
    intro: '此畫面直接使用正式 ConfirmProcessingExecution command。員工、採購批次、路線模組、來源、輸入儲位與輸出定義皆由 purpose-specific query 提供，不手動輸入 UUID，也不在前端補充未確認的加工 Business Rule。',
    locale: '介面語言',
    workDate: '工作日期',
    employee: '員工',
    employeeSearch: '搜尋員工名稱',
    employeeSelect: '選擇員工',
    batch: '採購批次',
    batchSearch: '搜尋批次產品',
    batchSelect: '選擇 ACTIVE 採購批次',
    module: '加工模組',
    moduleSearch: '搜尋模組名稱',
    moduleSelect: '選擇此批次路線的加工模組',
    executionMode: '執行模式',
    source: '原料來源',
    sourceSelect: '選擇來源類型',
    supplierSource: '供應商',
    farmersCombinedSource: '農戶合併',
    supplier: '供應商',
    supplierSearch: '搜尋供應商名稱',
    supplierSelect: '選擇供應商',
    inputScale: '輸入秤重讀值',
    inputContainerCount: '輸入實際容器數',
    inputContainerDefault: '模組提供的預設容器數',
    inputLocation: '輸入儲位',
    inputLocationSearch: '搜尋目前有來源庫存的儲位',
    inputLocationSelect: '選擇明確輸入儲位',
    inputLocationAuto: '目前只有一個候選儲位；可留空交由伺服器自動解析',
    inputLocationRequired: '目前無法唯一解析輸入儲位；確認前必須明確選擇。',
    inputLocationNone: '目前 query 沒有可選的來源庫存儲位。',
    outputs: '模組輸出',
    outputIntro: '輸出列由 ProcessingModuleOutput 定義產生；畫面不允許自行增加或刪除輸出。',
    outputLocationSearch: '搜尋輸出儲位名稱 / code',
    outputSequence: '輸出',
    outputKind: '輸出種類',
    target: '目標',
    targetUnavailable: '此輸出目標目前不可用，無法確認此 execution。',
    processMaterialScale: '輸出秤重讀值',
    outputContainerCount: '輸出實際容器數',
    completedQuantity: '完成數量',
    packagingWeight: '包裝重量',
    defaultWageRate: '既定 Wage Rate',
    outputLocation: '輸出儲位',
    outputLocationDefault: '使用目前可用的目標預設儲位',
    outputLocationSelect: '選擇明確輸出儲位',
    outputLocationRequired: '此輸出沒有可用的預設儲位；確認前必須明確選擇。',
    loading: '載入選項中…',
    queryFailed: '加工選項查詢失敗',
    submit: '確認加工執行',
    submitting: '確認中…',
    validation: '請檢查加工執行輸入資料。',
    employeeRequired: '請選擇員工。',
    batchRequired: '請選擇採購批次。',
    moduleRequired: '請選擇加工模組。',
    sourceRequired: 'SOURCE_TRACKED 模組必須選擇來源類型。',
    supplierRequired: '來源為供應商時必須選擇供應商。',
    inputConfigUnavailable: '此 SOURCE_TRACKED 模組沒有完整的輸入容器設定，不能由前端推測。',
    numberNonNegative: '數值必須是大於或等於 0 的有限數字。',
    countNonNegativeInteger: '容器數必須是大於或等於 0 的整數。',
    outputDefinitionUnavailable: '模組輸出定義目前不可執行。',
    insufficientInventory: '目前來源庫存不足以完成此加工執行。',
    negativePolicyUnsupported: '此模組的負庫存政策語意尚未被目前 executor 支援；前端不會自行推測政策。',
    idempotencyConflict: '此 Idempotency-Key 已被不同 command request 使用。請修改輸入或重新開始一次確認。',
    success: '加工執行已確認',
    executionId: 'Processing Execution',
    inventoryOperationId: 'Inventory Operation',
    rowVersion: 'Row Version',
    unexpected: '發生未預期錯誤。',
    sourceTracked: '來源追蹤',
    pooledOutput: '依輸出量回推消耗',
    finalPackaging: '最終包裝',
    processMaterial: '加工物料',
    salesProduct: '銷售產品',
  },
  'th-TH': {
    eyebrow: 'P6 · การแปรรูป',
    title: 'ยืนยันการดำเนินการแปรรูป',
    intro: 'หน้าจอนี้ใช้ ConfirmProcessingExecution จริง พนักงาน ล็อตจัดซื้อ โมดูลตาม route แหล่งวัตถุดิบ ตำแหน่งอินพุต และนิยามเอาต์พุตมาจาก purpose-specific query โดยไม่กรอก UUID เองและไม่สร้างกฎธุรกิจที่ยังไม่ได้ยืนยัน',
    locale: 'ภาษา',
    workDate: 'วันที่ทำงาน',
    employee: 'พนักงาน',
    employeeSearch: 'ค้นหาชื่อพนักงาน',
    employeeSelect: 'เลือกพนักงาน',
    batch: 'ล็อตจัดซื้อ',
    batchSearch: 'ค้นหาสินค้าในล็อต',
    batchSelect: 'เลือกล็อตจัดซื้อ ACTIVE',
    module: 'โมดูลแปรรูป',
    moduleSearch: 'ค้นหาชื่อโมดูล',
    moduleSelect: 'เลือกโมดูลตาม route ของล็อตนี้',
    executionMode: 'โหมดการทำงาน',
    source: 'แหล่งวัตถุดิบ',
    sourceSelect: 'เลือกชนิดแหล่งวัตถุดิบ',
    supplierSource: 'ซัพพลายเออร์',
    farmersCombinedSource: 'เกษตรกรรวม',
    supplier: 'ซัพพลายเออร์',
    supplierSearch: 'ค้นหาซัพพลายเออร์',
    supplierSelect: 'เลือกซัพพลายเออร์',
    inputScale: 'ค่าน้ำหนักขาเข้า',
    inputContainerCount: 'จำนวนภาชนะจริงขาเข้า',
    inputContainerDefault: 'จำนวนภาชนะตั้งต้นจากโมดูล',
    inputLocation: 'ตำแหน่งวัตถุดิบขาเข้า',
    inputLocationSearch: 'ค้นหาตำแหน่งที่มีสต็อกต้นทาง',
    inputLocationSelect: 'เลือกตำแหน่งขาเข้าให้ชัดเจน',
    inputLocationAuto: 'มีตำแหน่งผู้สมัครเพียงหนึ่งตำแหน่ง สามารถเว้นว่างให้เซิร์ฟเวอร์ resolve ได้',
    inputLocationRequired: 'ไม่สามารถ resolve ตำแหน่งขาเข้าได้เพียงค่าเดียว ต้องเลือกให้ชัดเจนก่อนยืนยัน',
    inputLocationNone: 'query ปัจจุบันไม่พบตำแหน่งสต็อกต้นทางที่เลือกได้',
    outputs: 'เอาต์พุตของโมดูล',
    outputIntro: 'รายการเอาต์พุตมาจาก ProcessingModuleOutput โดยตรง ไม่สามารถเพิ่มหรือลบเองในหน้าจอ',
    outputLocationSearch: 'ค้นหาชื่อ / code ตำแหน่งเอาต์พุต',
    outputSequence: 'เอาต์พุต',
    outputKind: 'ชนิดเอาต์พุต',
    target: 'เป้าหมาย',
    targetUnavailable: 'เป้าหมายของเอาต์พุตนี้ไม่พร้อมใช้งาน จึงยังยืนยัน execution ไม่ได้',
    processMaterialScale: 'ค่าน้ำหนักเอาต์พุต',
    outputContainerCount: 'จำนวนภาชนะจริงเอาต์พุต',
    completedQuantity: 'จำนวนที่เสร็จ',
    packagingWeight: 'น้ำหนักบรรจุภัณฑ์',
    defaultWageRate: 'Wage Rate ที่กำหนดไว้',
    outputLocation: 'ตำแหน่งเอาต์พุต',
    outputLocationDefault: 'ใช้ตำแหน่งหลักของเป้าหมายที่ยังใช้งานได้',
    outputLocationSelect: 'เลือกตำแหน่งเอาต์พุตให้ชัดเจน',
    outputLocationRequired: 'เอาต์พุตนี้ไม่มีตำแหน่งหลักที่ใช้งานได้ ต้องเลือกตำแหน่งให้ชัดเจนก่อนยืนยัน',
    loading: 'กำลังโหลดตัวเลือก…',
    queryFailed: 'โหลดตัวเลือกการแปรรูปไม่สำเร็จ',
    submit: 'ยืนยันการแปรรูป',
    submitting: 'กำลังยืนยัน…',
    validation: 'โปรดตรวจสอบข้อมูลการแปรรูป',
    employeeRequired: 'โปรดเลือกพนักงาน',
    batchRequired: 'โปรดเลือกล็อตจัดซื้อ',
    moduleRequired: 'โปรดเลือกโมดูลแปรรูป',
    sourceRequired: 'โมดูล SOURCE_TRACKED ต้องเลือกชนิดแหล่งวัตถุดิบ',
    supplierRequired: 'เมื่อแหล่งเป็นซัพพลายเออร์ ต้องเลือกซัพพลายเออร์',
    inputConfigUnavailable: 'โมดูล SOURCE_TRACKED นี้ไม่มีการตั้งค่าภาชนะขาเข้าที่สมบูรณ์ หน้าจอจะไม่คาดเดาค่าเอง',
    numberNonNegative: 'ค่าต้องเป็นตัวเลขจำกัดที่มากกว่าหรือเท่ากับ 0',
    countNonNegativeInteger: 'จำนวนภาชนะต้องเป็นจำนวนเต็มมากกว่าหรือเท่ากับ 0',
    outputDefinitionUnavailable: 'นิยามเอาต์พุตของโมดูลยังไม่พร้อมสำหรับ execution',
    insufficientInventory: 'สต็อกต้นทางปัจจุบันไม่เพียงพอสำหรับการแปรรูปนี้',
    negativePolicyUnsupported: 'ความหมายของนโยบายสต็อกติดลบของโมดูลนี้ยังไม่รองรับโดย executor ปัจจุบัน หน้าจอจะไม่คาดเดานโยบาย',
    idempotencyConflict: 'Idempotency-Key นี้ถูกใช้กับ command request อื่นแล้ว โปรดแก้ข้อมูลหรือเริ่มการยืนยันครั้งใหม่',
    success: 'ยืนยันการแปรรูปแล้ว',
    executionId: 'Processing Execution',
    inventoryOperationId: 'Inventory Operation',
    rowVersion: 'Row Version',
    unexpected: 'เกิดข้อผิดพลาดที่ไม่คาดคิด',
    sourceTracked: 'ติดตามแหล่งวัตถุดิบ',
    pooledOutput: 'คำนวณการใช้จากเอาต์พุต',
    finalPackaging: 'บรรจุขั้นสุดท้าย',
    processMaterial: 'วัตถุดิบระหว่างกระบวนการ',
    salesProduct: 'สินค้าขาย',
  },
} as const;

export function ProcessingExecutionPage() {
  const { locale, setLocale } = useOperationalLocale();
  const [employeeSearch, setEmployeeSearch] = useState('');
  const [batchSearch, setBatchSearch] = useState('');
  const [moduleSearch, setModuleSearch] = useState('');
  const [supplierSearch, setSupplierSearch] = useState('');
  const [inputLocationSearch, setInputLocationSearch] = useState('');
  const [outputLocationSearch, setOutputLocationSearch] = useState('');
  const [selectedEmployee, setSelectedEmployee] = useState<ProcessingEmployeeOption | null>(null);
  const [selectedBatch, setSelectedBatch] = useState<ProcessingBatchOption | null>(null);
  const [selectedModule, setSelectedModule] = useState<ProcessingModuleOption | null>(null);
  const [sourceKind, setSourceKind] = useState<ProcessingSourceKind | ''>('');
  const [selectedSupplier, setSelectedSupplier] = useState<ProcessingSupplierOption | null>(null);
  const [selectedInputLocation, setSelectedInputLocation] = useState<ProcessingStorageLocationOption | null>(null);
  const [inputScaleReading, setInputScaleReading] = useState('');
  const [inputContainerCount, setInputContainerCount] = useState('');
  const [outputDrafts, setOutputDrafts] = useState<Record<string, OutputDraft>>({});
  const [localError, setLocalError] = useState<string | null>(null);
  const submissionIdentity = useRef<SubmissionIdentity | null>(null);
  const labels = copy[locale];

  const deferredEmployeeSearch = useDeferredValue(employeeSearch);
  const deferredBatchSearch = useDeferredValue(batchSearch);
  const deferredModuleSearch = useDeferredValue(moduleSearch);
  const deferredSupplierSearch = useDeferredValue(supplierSearch);
  const deferredInputLocationSearch = useDeferredValue(inputLocationSearch);
  const deferredOutputLocationSearch = useDeferredValue(outputLocationSearch);

  const employeeQuery = useQuery({
    queryKey: ['processing-execution-options', 'employees', locale, deferredEmployeeSearch],
    queryFn: ({ signal }) => listProcessingEmployeeOptions({
      locale,
      search: deferredEmployeeSearch,
      limit: 50,
      signal,
    }),
    staleTime: 30_000,
  });

  const batchQuery = useQuery({
    queryKey: ['processing-execution-options', 'batches', locale, deferredBatchSearch],
    queryFn: ({ signal }) => listProcessingBatchOptions({
      locale,
      search: deferredBatchSearch,
      limit: 50,
      signal,
    }),
    staleTime: 30_000,
  });

  const moduleQuery = useQuery({
    queryKey: [
      'processing-execution-options',
      'modules',
      selectedBatch?.id ?? null,
      locale,
      deferredModuleSearch,
    ],
    queryFn: ({ signal }) => listProcessingModuleOptions(selectedBatch!.id, {
      locale,
      search: deferredModuleSearch,
      limit: 50,
      signal,
    }),
    enabled: selectedBatch !== null,
    staleTime: 30_000,
  });

  const supplierQuery = useQuery({
    queryKey: ['processing-execution-options', 'suppliers', locale, deferredSupplierSearch],
    queryFn: ({ signal }) => listProcessingSupplierOptions({
      locale,
      search: deferredSupplierSearch,
      limit: 50,
      signal,
    }),
    enabled: selectedModule?.executionMode === 'SOURCE_TRACKED' && sourceKind === 'SUPPLIER',
    staleTime: 30_000,
  });

  const inputLocationQuery = useQuery({
    queryKey: [
      'processing-execution-options',
      'input-storage-locations',
      selectedBatch?.id ?? null,
      selectedModule?.id ?? null,
      locale,
      deferredInputLocationSearch,
    ],
    queryFn: ({ signal }) => listProcessingInputStorageLocationOptions(
      selectedBatch!.id,
      selectedModule!.id,
      {
        locale,
        search: deferredInputLocationSearch,
        limit: 50,
        signal,
      },
    ),
    enabled: selectedBatch !== null && selectedModule !== null,
    staleTime: 15_000,
  });

  const moduleOutputsQuery = useQuery({
    queryKey: [
      'processing-execution-options',
      'module-outputs',
      selectedBatch?.id ?? null,
      selectedModule?.id ?? null,
      locale,
    ],
    queryFn: ({ signal }) => getProcessingModuleOutputOptions(
      selectedBatch!.id,
      selectedModule!.id,
      locale,
      signal,
    ),
    enabled: selectedBatch !== null && selectedModule !== null,
    staleTime: 30_000,
  });

  const outputLocationQuery = useQuery({
    queryKey: [
      'processing-execution-options',
      'storage-locations',
      locale,
      deferredOutputLocationSearch,
    ],
    queryFn: ({ signal }) => listProcessingStorageLocationOptions({
      locale,
      search: deferredOutputLocationSearch,
      limit: 100,
      signal,
    }),
    enabled: selectedModule !== null,
    staleTime: 30_000,
  });

  const mutation = useMutation({
    mutationFn: ({ request, idempotencyKey, locale: requestLocale }: Submission) =>
      confirmProcessingExecution(request, {
        idempotencyKey,
        locale: requestLocale,
      }),
  });

  const apiProblem = mutation.error instanceof ApiProblemError ? mutation.error : null;
  const problemMessage = useMemo(() => {
    if (apiProblem === null) {
      return mutation.error ? labels.unexpected : null;
    }

    switch (apiProblem.code) {
      case 'processing.input-location-required':
        return labels.inputLocationRequired;
      case 'processing.output-location-required':
        return labels.outputLocationRequired;
      case 'processing.source-shape-invalid':
        return labels.sourceRequired;
      case 'processing.input-scale-invalid':
      case 'processing.output-measurement-invalid':
        return labels.validation;
      case 'processing.output-definition-invalid':
        return labels.outputDefinitionUnavailable;
      case 'processing.insufficient-inventory':
        return labels.insufficientInventory;
      case 'processing.negative-inventory-policy-unsupported':
        return labels.negativePolicyUnsupported;
      case 'idempotency.key-reused':
        return labels.idempotencyConflict;
      default:
        return `${apiProblem.code}${apiProblem.problem.traceId ? ` · ${apiProblem.problem.traceId}` : ''}`;
    }
  }, [apiProblem, labels, mutation.error]);

  const optionError = firstOptionError(
    employeeQuery.error,
    batchQuery.error,
    moduleQuery.error,
    supplierQuery.error,
    inputLocationQuery.error,
    moduleOutputsQuery.error,
    outputLocationQuery.error,
  );
  const optionErrorMessage = optionError
    ? optionError instanceof ApiProblemError
      ? `${labels.queryFailed}: ${optionError.code}`
      : labels.queryFailed
    : null;

  const employeeItems = includeSelected(employeeQuery.data?.items ?? [], selectedEmployee);
  const batchItems = includeSelected(batchQuery.data?.items ?? [], selectedBatch);
  const moduleItems = includeSelected(moduleQuery.data?.items ?? [], selectedModule);
  const supplierItems = includeSelected(supplierQuery.data?.items ?? [], selectedSupplier);
  const inputLocationItems = includeSelected(inputLocationQuery.data?.items ?? [], selectedInputLocation);
  const outputs = moduleOutputsQuery.data?.outputs ?? [];
  const isSourceTracked = selectedModule?.executionMode === 'SOURCE_TRACKED';
  const inputConfigurationUnavailable = isSourceTracked && selectedModule?.inputUsesContainer === null;
  const autoInputLocation = inputLocationQuery.data?.autoSelectionLocationId
    ? inputLocationItems.find((item) => item.id === inputLocationQuery.data?.autoSelectionLocationId) ?? null
    : null;
  const requiresExplicitInputLocation =
    selectedModule !== null
    && inputLocationQuery.isSuccess
    && inputLocationQuery.data.autoSelectionLocationId === null;
  const outputDefinitionUnavailable = outputs.some((output) => !output.targetAvailable);
  const outputLocationItems = useMemo(() => {
    const visible = outputLocationQuery.data?.items ?? [];
    const visibleIds = new Set(visible.map((item) => item.id));
    const retainedById = new Map<string, ProcessingStorageLocationOption>();

    for (const draft of Object.values(outputDrafts)) {
      const location = draft.outputStorageLocation;
      if (location !== null && !visibleIds.has(location.id)) {
        retainedById.set(location.id, location);
      }
    }

    return [...visible, ...retainedById.values()];
  }, [outputDrafts, outputLocationQuery.data?.items]);

  const missingExplicitOutputLocation = outputs.some((output) =>
    !output.defaultStorageLocationAvailable
    && (outputDrafts[output.id]?.outputStorageLocationId ?? '') === '',
  );

  function resetDependentSelection() {
    setSelectedModule(null);
    setModuleSearch('');
    setSourceKind('');
    setSelectedSupplier(null);
    setSupplierSearch('');
    setSelectedInputLocation(null);
    setInputLocationSearch('');
    setInputScaleReading('');
    setInputContainerCount('');
    setOutputLocationSearch('');
    setOutputDrafts({});
  }

  function handleLocaleChange(next: OperationalLocale) {
    setLocale(next);
    setEmployeeSearch('');
    setBatchSearch('');
    setSelectedEmployee(null);
    setSelectedBatch(null);
    resetDependentSelection();
    mutation.reset();
    setLocalError(null);
  }

  function handleBatchChange(batchId: string) {
    setSelectedBatch(batchItems.find((item) => item.id === batchId) ?? null);
    resetDependentSelection();
    mutation.reset();
    setLocalError(null);
  }

  function handleModuleChange(moduleId: string) {
    const option = moduleItems.find((item) => item.id === moduleId) ?? null;
    setSelectedModule(option);
    setSourceKind('');
    setSelectedSupplier(null);
    setSupplierSearch('');
    setSelectedInputLocation(null);
    setInputLocationSearch('');
    setOutputLocationSearch('');
    setInputScaleReading('');
    setInputContainerCount(
      option?.defaultInputContainerCount !== null && option?.defaultInputContainerCount !== undefined
        ? String(option.defaultInputContainerCount)
        : '',
    );
    setOutputDrafts({});
    mutation.reset();
    setLocalError(null);
  }

  function handleSourceKindChange(next: ProcessingSourceKind | '') {
    setSourceKind(next);
    setSelectedSupplier(null);
    setSupplierSearch('');
    mutation.reset();
    setLocalError(null);
  }

  function patchOutputDraft(outputId: string, patch: Partial<OutputDraft>) {
    setOutputDrafts((current) => ({
      ...current,
      [outputId]: {
        ...(current[outputId] ?? emptyOutputDraft()),
        ...patch,
      },
    }));
    mutation.reset();
    setLocalError(null);
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setLocalError(null);
    mutation.reset();

    const formData = new FormData(event.currentTarget);

    try {
      if (selectedEmployee === null) throw new Error(labels.employeeRequired);
      if (selectedBatch === null) throw new Error(labels.batchRequired);
      if (selectedModule === null) throw new Error(labels.moduleRequired);
      if (inputConfigurationUnavailable) throw new Error(labels.inputConfigUnavailable);
      if (moduleOutputsQuery.data === undefined || outputs.length === 0) {
        throw new Error(labels.outputDefinitionUnavailable);
      }
      if (outputDefinitionUnavailable) throw new Error(labels.outputDefinitionUnavailable);
      if (requiresExplicitInputLocation && selectedInputLocation === null) {
        throw new Error(labels.inputLocationRequired);
      }

      let source: ConfirmProcessingExecutionRequest['source'] = null;
      let inputScale: ConfirmProcessingExecutionRequest['inputScale'] = null;

      if (selectedModule.executionMode === 'SOURCE_TRACKED') {
        if (sourceKind === '') throw new Error(labels.sourceRequired);
        if (sourceKind === 'SUPPLIER' && selectedSupplier === null) {
          throw new Error(labels.supplierRequired);
        }

        source = {
          sourceKind,
          supplierId: sourceKind === 'SUPPLIER' ? selectedSupplier!.id : null,
        };
        inputScale = {
          observedScaleReading: parseNonNegativeNumber(inputScaleReading, labels.numberNonNegative),
          actualContainerCount: selectedModule.inputUsesContainer
            ? parseNonNegativeInteger(inputContainerCount, labels.countNonNegativeInteger)
            : null,
        };
      }

      const request: ConfirmProcessingExecutionRequest = {
        workDate: requiredText(formData, 'workDate'),
        employeeId: selectedEmployee.id,
        procurementBatchId: selectedBatch.id,
        processingModuleId: selectedModule.id,
        source,
        inputScale,
        inputStorageLocationId: selectedInputLocation?.id ?? null,
        outputs: outputs.map((output) => createOutputMeasurement(output)),
      };

      const fingerprint = JSON.stringify(request);
      if (submissionIdentity.current?.fingerprint !== fingerprint) {
        submissionIdentity.current = {
          fingerprint,
          idempotencyKey: crypto.randomUUID(),
        };
      }

      mutation.mutate({
        request,
        idempotencyKey: submissionIdentity.current.idempotencyKey,
        locale,
      });
    } catch (error) {
      setLocalError(error instanceof Error ? error.message : labels.validation);
    }
  }

  function createOutputMeasurement(output: ProcessingModuleOutputOption) {
    const draft = resolveOutputDraft(output, outputDrafts);
    const outputStorageLocationId = draft.outputStorageLocationId || null;
    if (!output.defaultStorageLocationAvailable && outputStorageLocationId === null) {
      throw new Error(labels.outputLocationRequired);
    }

    if (output.outputKind === 'PROCESS_MATERIAL') {
      return {
        processingModuleOutputId: output.id,
        observedScaleReading: parseNonNegativeNumber(draft.observedScaleReading, labels.numberNonNegative),
        actualContainerCount: output.usesContainer
          ? parseNonNegativeInteger(draft.actualContainerCount, labels.countNonNegativeInteger)
          : null,
        completedQuantity: null,
        outputStorageLocationId,
      };
    }

    return {
      processingModuleOutputId: output.id,
      observedScaleReading: null,
      actualContainerCount: null,
      completedQuantity: parseNonNegativeNumber(draft.completedQuantity, labels.numberNonNegative),
      outputStorageLocationId,
    };
  }

  const submitDisabled =
    mutation.isPending
    || selectedEmployee === null
    || selectedBatch === null
    || selectedModule === null
    || moduleQuery.isPending
    || inputLocationQuery.isPending
    || moduleOutputsQuery.isPending
    || moduleOutputsQuery.isError
    || outputLocationQuery.isPending
    || outputLocationQuery.isError
    || inputConfigurationUnavailable
    || outputDefinitionUnavailable
    || outputs.length === 0
    || (isSourceTracked && sourceKind === '')
    || (isSourceTracked && sourceKind === 'SUPPLIER' && selectedSupplier === null)
    || (requiresExplicitInputLocation && selectedInputLocation === null)
    || missingExplicitOutputLocation;

  return (
    <section className="procurement-page" aria-labelledby="processing-execution-title">
      <header className="page-header">
        <div>
          <p className="eyebrow">{labels.eyebrow}</p>
          <h1 id="processing-execution-title">{labels.title}</h1>
        </div>

        <label className="locale-control">
          <span>{labels.locale}</span>
          <select value={locale} onChange={(event) => handleLocaleChange(event.target.value as OperationalLocale)}>
            <option value="zh-TW">繁體中文</option>
            <option value="th-TH">ไทย</option>
          </select>
        </label>
      </header>

      <div className="procurement-grid">
        <form className="entry-form" onSubmit={handleSubmit}>
          <div className="field-grid">
            <label>
              <span>{labels.workDate}</span>
              <input type="date" name="workDate" required />
            </label>

            <div className="option-picker">
              <span className="field-label">{labels.employee}</span>
              <input
                value={employeeSearch}
                onChange={(event) => setEmployeeSearch(event.target.value)}
                aria-label={labels.employeeSearch}
              />
              <select
                value={selectedEmployee?.id ?? ''}
                onChange={(event) => {
                  setSelectedEmployee(employeeItems.find((item) => item.id === event.target.value) ?? null);
                  mutation.reset();
                  setLocalError(null);
                }}
                required
              >
                <option value="">{employeeQuery.isPending ? labels.loading : labels.employeeSelect}</option>
                {employeeItems.map((item) => (
                  <option key={item.id} value={item.id}>{item.displayName}</option>
                ))}
              </select>
            </div>

            <div className="option-picker full-width">
              <span className="field-label">{labels.batch}</span>
              <input
                value={batchSearch}
                onChange={(event) => setBatchSearch(event.target.value)}
                aria-label={labels.batchSearch}
              />
              <select value={selectedBatch?.id ?? ''} onChange={(event) => handleBatchChange(event.target.value)} required>
                <option value="">{batchQuery.isPending ? labels.loading : labels.batchSelect}</option>
                {batchItems.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.procurementDate} · {item.procurementProductDisplayName}
                  </option>
                ))}
              </select>
            </div>

            <div className="option-picker full-width">
              <span className="field-label">{labels.module}</span>
              <input
                value={moduleSearch}
                onChange={(event) => setModuleSearch(event.target.value)}
                aria-label={labels.moduleSearch}
                disabled={selectedBatch === null}
              />
              <select
                value={selectedModule?.id ?? ''}
                onChange={(event) => handleModuleChange(event.target.value)}
                disabled={selectedBatch === null || moduleQuery.isPending}
                required
              >
                <option value="">{moduleQuery.isPending ? labels.loading : labels.moduleSelect}</option>
                {moduleItems.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.displayName} · {executionModeLabel(item.executionMode, labels)}
                  </option>
                ))}
              </select>
              {selectedModule && (
                <small className="default-hint">
                  {labels.executionMode}: {executionModeLabel(selectedModule.executionMode, labels)}
                </small>
              )}
              {inputConfigurationUnavailable && (
                <small className="required-hint">{labels.inputConfigUnavailable}</small>
              )}
            </div>

            {isSourceTracked && (
              <>
                <label>
                  <span>{labels.source}</span>
                  <select value={sourceKind} onChange={(event) => handleSourceKindChange(event.target.value as ProcessingSourceKind | '')} required>
                    <option value="">{labels.sourceSelect}</option>
                    <option value="SUPPLIER">{labels.supplierSource}</option>
                    <option value="FARMERS_COMBINED">{labels.farmersCombinedSource}</option>
                  </select>
                </label>

                {sourceKind === 'SUPPLIER' && (
                  <div className="option-picker">
                    <span className="field-label">{labels.supplier}</span>
                    <input
                      value={supplierSearch}
                      onChange={(event) => setSupplierSearch(event.target.value)}
                      aria-label={labels.supplierSearch}
                    />
                    <select
                      value={selectedSupplier?.id ?? ''}
                      onChange={(event) => {
                        setSelectedSupplier(supplierItems.find((item) => item.id === event.target.value) ?? null);
                        mutation.reset();
                        setLocalError(null);
                      }}
                      required
                    >
                      <option value="">{supplierQuery.isPending ? labels.loading : labels.supplierSelect}</option>
                      {supplierItems.map((item) => (
                        <option key={item.id} value={item.id}>{item.displayName}</option>
                      ))}
                    </select>
                  </div>
                )}

                <label>
                  <span>{labels.inputScale}</span>
                  <input
                    type="number"
                    min="0"
                    step="any"
                    inputMode="decimal"
                    value={inputScaleReading}
                    onChange={(event) => setInputScaleReading(event.target.value)}
                    required
                  />
                </label>

                {selectedModule?.inputUsesContainer && (
                  <label>
                    <span>{labels.inputContainerCount}</span>
                    <input
                      type="number"
                      min="0"
                      step="1"
                      inputMode="numeric"
                      value={inputContainerCount}
                      onChange={(event) => setInputContainerCount(event.target.value)}
                      required
                    />
                    {selectedModule.defaultInputContainerCount !== null && (
                      <small>{labels.inputContainerDefault}: {selectedModule.defaultInputContainerCount}</small>
                    )}
                  </label>
                )}
              </>
            )}

            <div className="option-picker full-width">
              <span className="field-label">{labels.inputLocation}</span>
              <input
                value={inputLocationSearch}
                onChange={(event) => setInputLocationSearch(event.target.value)}
                aria-label={labels.inputLocationSearch}
                disabled={selectedModule === null}
              />
              <select
                value={selectedInputLocation?.id ?? ''}
                onChange={(event) => {
                  setSelectedInputLocation(inputLocationItems.find((item) => item.id === event.target.value) ?? null);
                  mutation.reset();
                  setLocalError(null);
                }}
                disabled={selectedModule === null || inputLocationQuery.isPending}
                required={requiresExplicitInputLocation}
              >
                <option value="">
                  {inputLocationQuery.isPending
                    ? labels.loading
                    : inputLocationQuery.data?.autoSelectionLocationId
                      ? labels.inputLocationAuto
                      : labels.inputLocationSelect}
                </option>
                {inputLocationItems.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.displayName}{item.code ? ` · ${item.code}` : ''}
                  </option>
                ))}
              </select>
              {autoInputLocation && selectedInputLocation === null && (
                <small className="default-hint">
                  {labels.inputLocationAuto}: {autoInputLocation.displayName}
                </small>
              )}
              {requiresExplicitInputLocation && inputLocationItems.length > 0 && selectedInputLocation === null && (
                <small className="required-hint">{labels.inputLocationRequired}</small>
              )}
              {requiresExplicitInputLocation && inputLocationItems.length === 0 && (
                <small className="required-hint">{labels.inputLocationNone}</small>
              )}
            </div>

            {selectedModule && (
              <div className="processing-outputs full-width">
                <div className="processing-section-heading">
                  <div>
                    <span className="field-label">{labels.outputs}</span>
                  </div>
                  <input
                    value={outputLocationSearch}
                    onChange={(event) => setOutputLocationSearch(event.target.value)}
                    aria-label={labels.outputLocationSearch}
                  />
                </div>

                {moduleOutputsQuery.isPending && <p className="muted-copy">{labels.loading}</p>}

                {outputs.map((output) => {
                  const draft = resolveOutputDraft(output, outputDrafts);
                  return (
                    <article className="processing-output-card" key={output.id}>
                      <header className="processing-output-header">
                        <div>
                          <span className="processing-output-sequence">{labels.outputSequence} {output.outputSequence}</span>
                          <strong>{output.targetDisplayName ?? labels.outputDefinitionUnavailable}</strong>
                        </div>
                        <span className="mode-badge">{outputKindLabel(output.outputKind, labels)}</span>
                      </header>

                      <div className="processing-output-meta">
                        <span>{labels.defaultWageRate}: {formatNumber(locale, output.defaultWageRate)}</span>
                        {output.packagingWeight !== null && (
                          <span>{labels.packagingWeight}: {formatNumber(locale, output.packagingWeight)}</span>
                        )}
                      </div>

                      {!output.targetAvailable && (
                        <p className="required-hint">{labels.targetUnavailable}</p>
                      )}

                      <div className="field-grid processing-output-fields">
                        {output.outputKind === 'PROCESS_MATERIAL' ? (
                          <>
                            <label>
                              <span>{labels.processMaterialScale}</span>
                              <input
                                type="number"
                                min="0"
                                step="any"
                                inputMode="decimal"
                                value={draft.observedScaleReading}
                                onChange={(event) => patchOutputDraft(output.id, { observedScaleReading: event.target.value })}
                                required
                              />
                            </label>
                            {output.usesContainer && (
                              <label>
                                <span>{labels.outputContainerCount}</span>
                                <input
                                  type="number"
                                  min="0"
                                  step="1"
                                  inputMode="numeric"
                                  value={draft.actualContainerCount}
                                  onChange={(event) => patchOutputDraft(output.id, { actualContainerCount: event.target.value })}
                                  required
                                />
                              </label>
                            )}
                          </>
                        ) : (
                          <label>
                            <span>{labels.completedQuantity}</span>
                            <input
                              type="number"
                              min="0"
                              step="any"
                              inputMode="decimal"
                              value={draft.completedQuantity}
                              onChange={(event) => patchOutputDraft(output.id, { completedQuantity: event.target.value })}
                              required
                            />
                          </label>
                        )}

                        <label className="full-width">
                          <span>{labels.outputLocation}</span>
                          <select
                            value={draft.outputStorageLocationId}
                            onChange={(event) => {
                              const locationId = event.target.value;
                              patchOutputDraft(output.id, {
                                outputStorageLocationId: locationId,
                                outputStorageLocation:
                                  outputLocationItems.find((item) => item.id === locationId) ?? null,
                              });
                            }}
                            required={!output.defaultStorageLocationAvailable}
                          >
                            <option value="">
                              {output.defaultStorageLocationAvailable
                                ? labels.outputLocationDefault
                                : labels.outputLocationSelect}
                            </option>
                            {outputLocationItems.map((item) => (
                              <option key={item.id} value={item.id}>
                                {item.displayName}{item.code ? ` · ${item.code}` : ''}
                              </option>
                            ))}
                          </select>
                          {!output.defaultStorageLocationAvailable && draft.outputStorageLocationId === '' && (
                            <small className="required-hint">{labels.outputLocationRequired}</small>
                          )}
                        </label>
                      </div>
                    </article>
                  );
                })}
              </div>
            )}
          </div>

          {(localError ?? problemMessage ?? optionErrorMessage) && (
            <div className="problem-banner" role="alert">
              {localError ?? problemMessage ?? optionErrorMessage}
            </div>
          )}

          <button className="primary-action" type="submit" disabled={submitDisabled}>
            {mutation.isPending ? labels.submitting : labels.submit}
          </button>
        </form>

        <aside className="result-panel" aria-live="polite">
          {mutation.data ? (
            <>
              <p className="eyebrow">{labels.success}</p>
              <dl>
                <ResultRow label={labels.executionId} value={mutation.data.processingExecutionId} />
                <ResultRow label={labels.inventoryOperationId} value={mutation.data.inventoryOperationId} />
                <ResultRow label={labels.rowVersion} value={String(mutation.data.processingExecutionRowVersion)} />
              </dl>
            </>
          ) : (
            <div className="result-placeholder" aria-hidden="true">
              <span>YowThi ERP V2</span>
            </div>
          )}
        </aside>
      </div>
    </section>
  );
}

function resolveOutputDraft(
  output: ProcessingModuleOutputOption,
  drafts: Record<string, OutputDraft>,
): OutputDraft {
  return drafts[output.id] ?? {
    ...emptyOutputDraft(),
    actualContainerCount:
      output.usesContainer && output.defaultContainerCount !== null
        ? String(output.defaultContainerCount)
        : '',
  };
}

function ResultRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="result-row">
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

function executionModeLabel(mode: ProcessingModuleOption['executionMode'], labels: typeof copy[OperationalLocale]) {
  switch (mode) {
    case 'SOURCE_TRACKED':
      return labels.sourceTracked;
    case 'POOLED_OUTPUT':
      return labels.pooledOutput;
    case 'FINAL_PACKAGING':
      return labels.finalPackaging;
  }
}

function outputKindLabel(kind: ProcessingModuleOutputOption['outputKind'], labels: typeof copy[OperationalLocale]) {
  return kind === 'PROCESS_MATERIAL' ? labels.processMaterial : labels.salesProduct;
}

function includeSelected<T extends { id: string }>(items: T[], selected: T | null): T[] {
  if (selected === null || items.some((item) => item.id === selected.id)) {
    return items;
  }

  return [selected, ...items];
}

function firstOptionError(...errors: Array<Error | null>): Error | null {
  return errors.find((error) => error !== null) ?? null;
}

function requiredText(formData: FormData, name: string): string {
  const value = formData.get(name);
  if (typeof value !== 'string' || value.trim() === '') {
    throw new Error(`${name} is required.`);
  }
  return value.trim();
}

function parseNonNegativeNumber(raw: string, message: string): number {
  if (raw.trim() === '') throw new Error(message);
  const value = Number(raw);
  if (!Number.isFinite(value) || value < 0) throw new Error(message);
  return value;
}

function parseNonNegativeInteger(raw: string, message: string): number {
  const value = parseNonNegativeNumber(raw, message);
  if (!Number.isInteger(value)) throw new Error(message);
  return value;
}

function formatNumber(locale: OperationalLocale, value: number): string {
  return new Intl.NumberFormat(locale, { maximumFractionDigits: 6 }).format(value);
}
