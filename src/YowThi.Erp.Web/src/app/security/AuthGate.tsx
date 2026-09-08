import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { PropsWithChildren } from 'react';

import { useOperationalLocale } from '../i18n/locale';
import { developmentLogin, getAuthenticationSession } from './authSession';
import './authGate.css';

const copy = {
  'zh-TW': {
    brand: 'YowThi ERP',
    title: '登入',
    loading: '正在確認登入狀態',
    unavailable: '目前沒有可用的登入方式',
    failed: '無法連線至 ERP 服務',
    retry: '重試',
    developmentLogin: '測試管理員登入',
  },
  'th-TH': {
    brand: 'YowThi ERP',
    title: 'เข้าสู่ระบบ',
    loading: 'กำลังตรวจสอบสถานะการเข้าสู่ระบบ',
    unavailable: 'ยังไม่มีวิธีเข้าสู่ระบบที่พร้อมใช้งาน',
    failed: 'ไม่สามารถเชื่อมต่อบริการ ERP ได้',
    retry: 'ลองใหม่',
    developmentLogin: 'เข้าสู่ระบบผู้ดูแลทดสอบ',
  },
} as const;

export function AuthGate({ children }: PropsWithChildren) {
  const { locale } = useOperationalLocale();
  const text = copy[locale];
  const queryClient = useQueryClient();
  const sessionQuery = useQuery({
    queryKey: ['authentication-session'],
    queryFn: ({ signal }) => getAuthenticationSession(signal),
    retry: false,
    staleTime: 10_000,
  });
  const loginMutation = useMutation({
    mutationFn: developmentLogin,
    onSuccess: (session) => {
      queryClient.setQueryData(['authentication-session'], session);
    },
  });

  if (sessionQuery.isPending) {
    return (
      <main className="auth-gate">
        <section className="auth-card" aria-busy="true">
          <strong>{text.brand}</strong>
          <h1>{text.title}</h1>
          <p>{text.loading}</p>
        </section>
      </main>
    );
  }

  if (sessionQuery.isError) {
    return (
      <main className="auth-gate">
        <section className="auth-card">
          <strong>{text.brand}</strong>
          <h1>{text.title}</h1>
          <p>{text.failed}</p>
          <button type="button" className="primary-action" onClick={() => void sessionQuery.refetch()}>
            {text.retry}
          </button>
        </section>
      </main>
    );
  }

  if (!sessionQuery.data.authenticated) {
    return (
      <main className="auth-gate">
        <section className="auth-card">
          <strong>{text.brand}</strong>
          <h1>{text.title}</h1>
          {sessionQuery.data.developmentLoginAvailable ? (
            <button
              type="button"
              className="primary-action auth-development-login"
              disabled={loginMutation.isPending}
              onClick={() => loginMutation.mutate()}
            >
              {text.developmentLogin}
            </button>
          ) : (
            <p>{text.unavailable}</p>
          )}
          {loginMutation.isError ? <p className="auth-error">{text.failed}</p> : null}
        </section>
      </main>
    );
  }

  return children;
}
