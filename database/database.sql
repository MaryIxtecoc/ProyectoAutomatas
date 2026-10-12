-- =============================================================================
-- AutomatasFinal (Léxica) - Base de datos PostgreSQL / Supabase
-- Ejecutar completo en una base vacía (Supabase: SQL Editor -> Run).
-- =============================================================================

-- Extensiones
create extension if not exists citext;      -- correo y nickname sin distinguir mayúsculas

-- Esquema de funciones auxiliares (contexto de sesión para RLS)
create schema if not exists app;

-- -----------------------------------------------------------------------------
-- Tipos
-- -----------------------------------------------------------------------------
CREATE TYPE public.analysis_language AS ENUM (
    'ES',
    'EN',
    'RU',
    'ZH',
    'AR'
);

CREATE TYPE public.analysis_status AS ENUM (
    'PENDING',
    'PROCESSING',
    'COMPLETED',
    'FAILED'
);

CREATE TYPE public.notification_method AS ENUM (
    'email',
    'whatsapp',
    'both'
);

-- -----------------------------------------------------------------------------
-- Tablas
-- -----------------------------------------------------------------------------
-- Roles del sistema (ADMIN, SUPERVISOR, ANALISTA)
CREATE TABLE public.roles (
    id smallint NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    description text,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);
ALTER TABLE public.roles ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.roles_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

-- Usuarios registrados; contraseña solo como hash
CREATE TABLE public.users (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    email public.citext NOT NULL,
    phone text NOT NULL,
    birth_date date NOT NULL,
    nickname public.citext NOT NULL,
    password_hash text NOT NULL,
    role_id smallint NOT NULL,
    notification_method public.notification_method DEFAULT 'email'::public.notification_method NOT NULL,
    photo_original_path text,
    photo_modified_path text,
    is_active boolean DEFAULT true NOT NULL,
    deleted_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    last_login_at timestamp with time zone,
    CONSTRAINT users_birth_date_sane_chk CHECK ((birth_date > '1900-01-01'::date)),
    CONSTRAINT users_email_format_chk CHECK ((email OPERATOR(public.~*) '^[^@\s]+@[^@\s]+\.[^@\s]+$'::public.citext)),
    CONSTRAINT users_phone_not_blank_chk CHECK ((length(TRIM(BOTH FROM phone)) > 0))
);

-- Descriptor facial (SFace) para el login facial
CREATE TABLE public.user_biometrics (
    user_id uuid NOT NULL,
    face_descriptor jsonb NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);

-- Credenciales con QR; qr_code_value = sha256 del secreto del QR
CREATE TABLE public.user_credentials (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    qr_code_value text NOT NULL,
    pdf_path text,
    is_active boolean DEFAULT true NOT NULL,
    issued_at timestamp with time zone DEFAULT now() NOT NULL
);

-- Apariencia de la credencial elegida por el usuario
CREATE TABLE public.credential_designs (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    template character varying(50) DEFAULT 'CLASSIC'::character varying NOT NULL,
    background_color character varying(20),
    accent_color character varying(20),
    text_color character varying(20),
    photo_style character varying(50),
    qr_style character varying(50),
    decorations jsonb,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);

-- Solicitudes de corrección de correo / fecha de nacimiento
CREATE TABLE public.profile_change_requests (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    requested_email character varying(255),
    requested_birth_date date,
    reason text NOT NULL,
    status character varying(20) DEFAULT 'PENDING'::character varying NOT NULL,
    reviewed_by uuid,
    reviewed_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);

-- Tokens de restablecimiento (solo hash)
CREATE TABLE public.password_reset_tokens (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    token_hash text NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    used_at timestamp with time zone,
    requested_ip inet,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);

-- Sesiones (una por login); permiten cerrar sesiones
CREATE TABLE public.user_sessions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    login_method text NOT NULL,
    ip_address inet,
    user_agent text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    last_seen_at timestamp with time zone DEFAULT now() NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone,
    revoked_by uuid,
    revoke_reason text
);

-- Archivos cargados para análisis y su estado
CREATE TABLE public.analysis_jobs (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    file_name text NOT NULL,
    file_storage_path text NOT NULL,
    language public.analysis_language NOT NULL,
    status public.analysis_status DEFAULT 'PENDING'::public.analysis_status NOT NULL,
    report_requested boolean DEFAULT false NOT NULL,
    report_pdf_path text,
    error_message text,
    uploaded_at timestamp with time zone DEFAULT now() NOT NULL,
    processed_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT analysis_jobs_processed_after_upload_chk CHECK (((processed_at IS NULL) OR (processed_at >= uploaded_at)))
);

-- Resultado del análisis léxico
CREATE TABLE public.analysis_results (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    job_id uuid NOT NULL,
    total_word_count integer DEFAULT 0 NOT NULL,
    most_frequent_words jsonb,
    least_frequent_words jsonb,
    personal_pronouns jsonb,
    person_names jsonb,
    root_nouns jsonb,
    root_verbs jsonb,
    other_classifications jsonb,
    full_result jsonb NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT analysis_results_word_count_chk CHECK ((total_word_count >= 0))
);

-- Diccionario de entidades del analizador (parametrizable por ADMIN)
CREATE TABLE public.entity_dictionary (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    value text NOT NULL,
    normalized_value text NOT NULL,
    canonical_value text,
    entity_type text NOT NULL,
    language text,
    source text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT entity_dictionary_type_check CHECK ((entity_type = ANY (ARRAY['PERSON'::text, 'COUNTRY'::text, 'CITY'::text, 'ORGANIZATION'::text, 'BRAND'::text, 'PRODUCT'::text, 'LOCATION'::text])))
);

-- Bitácora de auditoría
CREATE TABLE public.audit_logs (
    id bigint NOT NULL,
    user_id uuid,
    action text NOT NULL,
    details jsonb,
    ip_address inet,
    user_agent text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    target_user_id uuid,
    success boolean
);
ALTER TABLE public.audit_logs ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.audit_logs_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

-- -----------------------------------------------------------------------------
-- Llaves primarias y únicas
-- -----------------------------------------------------------------------------
ALTER TABLE public.analysis_jobs
    ADD CONSTRAINT analysis_jobs_pkey PRIMARY KEY (id);
ALTER TABLE public.analysis_results
    ADD CONSTRAINT analysis_results_pkey PRIMARY KEY (id);
ALTER TABLE public.audit_logs
    ADD CONSTRAINT audit_logs_pkey PRIMARY KEY (id);
ALTER TABLE public.credential_designs
    ADD CONSTRAINT credential_designs_pkey PRIMARY KEY (id);
ALTER TABLE public.entity_dictionary
    ADD CONSTRAINT entity_dictionary_pkey PRIMARY KEY (id);
ALTER TABLE public.password_reset_tokens
    ADD CONSTRAINT password_reset_tokens_pkey PRIMARY KEY (id);
ALTER TABLE public.profile_change_requests
    ADD CONSTRAINT profile_change_requests_pkey PRIMARY KEY (id);
ALTER TABLE public.roles
    ADD CONSTRAINT roles_code_key UNIQUE (code);
ALTER TABLE public.roles
    ADD CONSTRAINT roles_pkey PRIMARY KEY (id);
ALTER TABLE public.user_biometrics
    ADD CONSTRAINT user_biometrics_pkey PRIMARY KEY (user_id);
ALTER TABLE public.user_credentials
    ADD CONSTRAINT user_credentials_pkey PRIMARY KEY (id);
ALTER TABLE public.user_sessions
    ADD CONSTRAINT user_sessions_pkey PRIMARY KEY (id);
ALTER TABLE public.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);

-- -----------------------------------------------------------------------------
-- Llaves foráneas
-- -----------------------------------------------------------------------------
ALTER TABLE public.analysis_jobs
    ADD CONSTRAINT analysis_jobs_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id);
ALTER TABLE public.analysis_results
    ADD CONSTRAINT analysis_results_job_id_fkey FOREIGN KEY (job_id) REFERENCES public.analysis_jobs(id) ON DELETE CASCADE;
ALTER TABLE public.audit_logs
    ADD CONSTRAINT audit_logs_target_user_id_fkey FOREIGN KEY (target_user_id) REFERENCES public.users(id) ON DELETE SET NULL;
ALTER TABLE public.audit_logs
    ADD CONSTRAINT audit_logs_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE SET NULL;
ALTER TABLE public.credential_designs
    ADD CONSTRAINT fk_credential_design_user FOREIGN KEY (user_id) REFERENCES public.users(id);
ALTER TABLE public.profile_change_requests
    ADD CONSTRAINT fk_profile_change_reviewer FOREIGN KEY (reviewed_by) REFERENCES public.users(id);
ALTER TABLE public.profile_change_requests
    ADD CONSTRAINT fk_profile_change_user FOREIGN KEY (user_id) REFERENCES public.users(id);
ALTER TABLE public.password_reset_tokens
    ADD CONSTRAINT password_reset_tokens_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;
ALTER TABLE public.user_biometrics
    ADD CONSTRAINT user_biometrics_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;
ALTER TABLE public.user_credentials
    ADD CONSTRAINT user_credentials_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;
ALTER TABLE public.user_sessions
    ADD CONSTRAINT user_sessions_revoked_by_fkey FOREIGN KEY (revoked_by) REFERENCES public.users(id) ON DELETE SET NULL;
ALTER TABLE public.user_sessions
    ADD CONSTRAINT user_sessions_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;
ALTER TABLE public.users
    ADD CONSTRAINT users_role_id_fkey FOREIGN KEY (role_id) REFERENCES public.roles(id);

-- -----------------------------------------------------------------------------
-- Índices
-- -----------------------------------------------------------------------------
CREATE INDEX analysis_jobs_status_idx ON public.analysis_jobs USING btree (status);
CREATE INDEX analysis_jobs_uploaded_at_idx ON public.analysis_jobs USING btree (uploaded_at DESC);
CREATE INDEX analysis_jobs_user_uploaded_idx ON public.analysis_jobs USING btree (user_id, uploaded_at DESC);
CREATE UNIQUE INDEX analysis_results_job_uk ON public.analysis_results USING btree (job_id);
CREATE INDEX audit_logs_action_idx ON public.audit_logs USING btree (action);
CREATE INDEX audit_logs_created_at_idx ON public.audit_logs USING btree (created_at DESC);
CREATE INDEX audit_logs_target_created_idx ON public.audit_logs USING btree (target_user_id, created_at DESC);
CREATE INDEX audit_logs_user_created_idx ON public.audit_logs USING btree (user_id, created_at DESC);
CREATE INDEX idx_entity_dictionary_normalized ON public.entity_dictionary USING btree (normalized_value);
CREATE INDEX idx_entity_dictionary_normalized_type ON public.entity_dictionary USING btree (normalized_value, entity_type);
CREATE INDEX idx_entity_dictionary_type ON public.entity_dictionary USING btree (entity_type);
CREATE INDEX password_reset_tokens_expires_idx ON public.password_reset_tokens USING btree (expires_at);
CREATE UNIQUE INDEX password_reset_tokens_hash_uk ON public.password_reset_tokens USING btree (token_hash);
CREATE INDEX password_reset_tokens_user_idx ON public.password_reset_tokens USING btree (user_id);
CREATE UNIQUE INDEX user_credentials_one_active_uk ON public.user_credentials USING btree (user_id) WHERE is_active;
CREATE UNIQUE INDEX user_credentials_qr_uk ON public.user_credentials USING btree (qr_code_value);
CREATE INDEX user_sessions_open_idx ON public.user_sessions USING btree (expires_at) WHERE (revoked_at IS NULL);
CREATE INDEX user_sessions_user_idx ON public.user_sessions USING btree (user_id, created_at DESC);
CREATE INDEX users_active_idx ON public.users USING btree (id) WHERE (deleted_at IS NULL);
CREATE UNIQUE INDEX users_email_uk ON public.users USING btree (email);
CREATE UNIQUE INDEX users_nickname_uk ON public.users USING btree (nickname);
CREATE INDEX users_role_id_idx ON public.users USING btree (role_id);
create unique index credential_designs_user_uk on public.credential_designs (user_id);  -- un diseño por usuario

-- -----------------------------------------------------------------------------
-- Funciones (app.*) y triggers
-- -----------------------------------------------------------------------------
-- Contexto de la sesión (lo usan las políticas RLS)
CREATE FUNCTION app.auth_context() RETURNS text
    LANGUAGE sql STABLE
    AS $$
  select nullif(current_setting('app.auth_context', true), '');
$$;

CREATE FUNCTION app.current_user_id() RETURNS uuid
    LANGUAGE sql STABLE
    AS $$
  select nullif(current_setting('app.current_user_id', true), '')::uuid;
$$;

CREATE FUNCTION app.current_user_role() RETURNS text
    LANGUAGE sql STABLE
    AS $$
  select nullif(current_setting('app.current_user_role', true), '');
$$;

-- Mantiene updated_at al día
CREATE FUNCTION app.set_updated_at() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
begin
  new.updated_at = now();
  return new;
end;
$$;


SET default_tablespace = '';

SET default_table_access_method = heap;

-- Confirmación de restablecimiento
CREATE FUNCTION app.confirm_password_reset(p_token_hash text, p_new_password_hash text) RETURNS TABLE(success boolean, user_id uuid)
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public', 'pg_temp'
    AS $$
declare
  v_token password_reset_tokens%rowtype;
begin
  select * into v_token
  from password_reset_tokens t
  where t.token_hash = p_token_hash
    and t.used_at is null
    and t.expires_at > now()
  limit 1;

  if not found then
    return query select false, null::uuid;
    return;
  end if;

  update users
     set password_hash = p_new_password_hash
   where users.id = v_token.user_id;

  update password_reset_tokens
     set used_at = now()
   where password_reset_tokens.id = v_token.id;

  return query select true, v_token.user_id;
end;
$$;

-- Login facial (1:1)
CREATE FUNCTION app.find_user_for_facial_login(p_identifier public.citext) RETURNS TABLE(id uuid, nickname public.citext, face_descriptor jsonb, role_code text, is_active boolean, deleted_at timestamp with time zone)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'public', 'pg_temp'
    AS $$
  select u.id, u.nickname, b.face_descriptor, r.code, u.is_active, u.deleted_at
  from users u
  join roles r on r.id = u.role_id
  join user_biometrics b on b.user_id = u.id
  where u.email = p_identifier or u.nickname = p_identifier
  limit 1;
$$;

-- Login con contraseña
CREATE FUNCTION app.find_user_for_password_login(p_identifier public.citext) RETURNS TABLE(id uuid, nickname public.citext, password_hash text, role_code text, is_active boolean, deleted_at timestamp with time zone)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'public', 'pg_temp'
    AS $$
  select u.id, u.nickname, u.password_hash, r.code, u.is_active, u.deleted_at
  from users u
  join roles r on r.id = u.role_id
  where u.email = p_identifier or u.nickname = p_identifier
  limit 1;
$$;

-- Login con QR (compara el hash del secreto)
CREATE FUNCTION app.find_user_for_qr_login(p_qr_code_value text) RETURNS TABLE(id uuid, nickname public.citext, role_code text, is_active boolean, deleted_at timestamp with time zone)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'public', 'pg_temp'
    AS $$
  select u.id, u.nickname, r.code, u.is_active, u.deleted_at
  from user_credentials c
  join users u on u.id = c.user_id
  join roles r on r.id = u.role_id
  where c.qr_code_value = 'sha256:' || encode(sha256(convert_to(p_qr_code_value, 'UTF8')), 'hex')
    and c.is_active
  limit 1;
$$;

-- Registro (SECURITY DEFINER)
CREATE FUNCTION app.register_user(p_email public.citext, p_phone text, p_birth_date date, p_nickname public.citext, p_password_hash text, p_notification_method public.notification_method, p_photo_original_path text DEFAULT NULL::text, p_photo_modified_path text DEFAULT NULL::text, p_face_descriptor jsonb DEFAULT NULL::jsonb, p_qr_code_value text DEFAULT NULL::text, p_credential_pdf_path text DEFAULT NULL::text) RETURNS TABLE(id uuid, nickname public.citext, role_code text)
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public', 'pg_temp'
    AS $$
declare
  v_role_id smallint;
  v_user_id uuid;
begin
  if p_email is null or p_nickname is null or p_password_hash is null then
    raise exception 'email, nickname y password_hash son obligatorios';
  end if;

  select r.id into v_role_id from roles r where r.code = 'ANALISTA';
  if v_role_id is null then
    raise exception 'Rol ANALISTA no está configurado';
  end if;

  insert into users (
    email, phone, birth_date, nickname, password_hash,
    role_id, notification_method, photo_original_path, photo_modified_path
  ) values (
    p_email, p_phone, p_birth_date, p_nickname, p_password_hash,
    v_role_id, p_notification_method, p_photo_original_path, p_photo_modified_path
  )
  returning users.id into v_user_id;

  if p_face_descriptor is not null then
    insert into user_biometrics (user_id, face_descriptor)
    values (v_user_id, p_face_descriptor);
  end if;

  if p_qr_code_value is not null then
    insert into user_credentials (user_id, qr_code_value, pdf_path)
    values (v_user_id, p_qr_code_value, p_credential_pdf_path);
  end if;

  return query
    select v_user_id, p_nickname, 'ANALISTA'::text;
end;
$$;

-- Solicitud de restablecimiento
CREATE FUNCTION app.request_password_reset(p_identifier public.citext, p_token_hash text, p_expires_at timestamp with time zone, p_requested_ip inet DEFAULT NULL::inet) RETURNS TABLE(user_id uuid, notification_method public.notification_method, email public.citext, phone text)
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public', 'pg_temp'
    AS $$
declare
  v_user users%rowtype;
begin
  select * into v_user
  from users u
  where (u.email = p_identifier or u.nickname = p_identifier)
    and u.deleted_at is null
  limit 1;

  if not found then
    return; -- 0 filas: el backend debe dar una respuesta genérica igualmente
  end if;

  insert into password_reset_tokens (user_id, token_hash, expires_at, requested_ip)
  values (v_user.id, p_token_hash, p_expires_at, p_requested_ip);

  return query
    select v_user.id, v_user.notification_method, v_user.email, v_user.phone;
end;
$$;

CREATE TRIGGER trg_analysis_jobs_updated_at BEFORE UPDATE ON public.analysis_jobs FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();
CREATE TRIGGER trg_user_biometrics_updated_at BEFORE UPDATE ON public.user_biometrics FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();
CREATE TRIGGER trg_users_updated_at BEFORE UPDATE ON public.users FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

-- -----------------------------------------------------------------------------
-- Seguridad: RLS. Sin políticas públicas; el backend usa un rol que no pasa por RLS.
-- -----------------------------------------------------------------------------
ALTER TABLE public.analysis_jobs ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.analysis_results ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.audit_logs ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.password_reset_tokens ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.roles ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.user_biometrics ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.user_credentials ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.user_sessions ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.users ENABLE ROW LEVEL SECURITY;
alter table public.credential_designs      enable row level security;
alter table public.profile_change_requests enable row level security;
alter table public.entity_dictionary       enable row level security;

CREATE POLICY analysis_jobs_insert ON public.analysis_jobs FOR INSERT WITH CHECK (((app.auth_context() = 'AUTHENTICATED'::text) AND ((user_id = app.current_user_id()) OR (app.current_user_role() = 'ADMIN'::text))));
CREATE POLICY analysis_jobs_select ON public.analysis_jobs FOR SELECT USING (((app.auth_context() = 'AUTHENTICATED'::text) AND ((user_id = app.current_user_id()) OR (app.current_user_role() = ANY (ARRAY['ADMIN'::text, 'SUPERVISOR'::text])))));
CREATE POLICY analysis_jobs_update ON public.analysis_jobs FOR UPDATE USING (((app.auth_context() = 'AUTHENTICATED'::text) AND ((user_id = app.current_user_id()) OR (app.current_user_role() = 'ADMIN'::text)))) WITH CHECK (((app.auth_context() = 'AUTHENTICATED'::text) AND ((user_id = app.current_user_id()) OR (app.current_user_role() = 'ADMIN'::text))));
CREATE POLICY analysis_results_insert ON public.analysis_results FOR INSERT WITH CHECK (((app.auth_context() = 'AUTHENTICATED'::text) AND (app.current_user_role() = 'ADMIN'::text)));
CREATE POLICY analysis_results_select ON public.analysis_results FOR SELECT USING (((app.auth_context() = 'AUTHENTICATED'::text) AND ((app.current_user_role() = ANY (ARRAY['ADMIN'::text, 'SUPERVISOR'::text])) OR (EXISTS ( SELECT 1
   FROM public.analysis_jobs j
  WHERE ((j.id = analysis_results.job_id) AND (j.user_id = app.current_user_id())))))));
CREATE POLICY analysis_results_update ON public.analysis_results FOR UPDATE USING (((app.auth_context() = 'AUTHENTICATED'::text) AND (app.current_user_role() = 'ADMIN'::text))) WITH CHECK (((app.auth_context() = 'AUTHENTICATED'::text) AND (app.current_user_role() = 'ADMIN'::text)));
CREATE POLICY audit_logs_insert_always ON public.audit_logs FOR INSERT WITH CHECK (true);
CREATE POLICY audit_logs_select_admin_only ON public.audit_logs FOR SELECT USING (((app.auth_context() = 'AUTHENTICATED'::text) AND (app.current_user_role() = 'ADMIN'::text)));
CREATE POLICY roles_insert_admin ON public.roles FOR INSERT WITH CHECK (((app.auth_context() = 'AUTHENTICATED'::text) AND (app.current_user_role() = 'ADMIN'::text)));
CREATE POLICY roles_select_all ON public.roles FOR SELECT USING (true);
CREATE POLICY roles_update_admin ON public.roles FOR UPDATE USING (((app.auth_context() = 'AUTHENTICATED'::text) AND (app.current_user_role() = 'ADMIN'::text))) WITH CHECK (((app.auth_context() = 'AUTHENTICATED'::text) AND (app.current_user_role() = 'ADMIN'::text)));
CREATE POLICY user_biometrics_insert ON public.user_biometrics FOR INSERT WITH CHECK (((app.auth_context() = 'AUTHENTICATED'::text) AND ((user_id = app.current_user_id()) OR (app.current_user_role() = 'ADMIN'::text))));
CREATE POLICY user_biometrics_select ON public.user_biometrics FOR SELECT USING (((app.auth_context() = 'AUTHENTICATED'::text) AND ((user_id = app.current_user_id()) OR (app.current_user_role() = 'ADMIN'::text))));
CREATE POLICY user_biometrics_update ON public.user_biometrics FOR UPDATE USING (((app.auth_context() = 'AUTHENTICATED'::text) AND ((user_id = app.current_user_id()) OR (app.current_user_role() = 'ADMIN'::text)))) WITH CHECK (((app.auth_context() = 'AUTHENTICATED'::text) AND ((user_id = app.current_user_id()) OR (app.current_user_role() = 'ADMIN'::text))));
CREATE POLICY user_credentials_insert ON public.user_credentials FOR INSERT WITH CHECK (((app.auth_context() = 'AUTHENTICATED'::text) AND ((user_id = app.current_user_id()) OR (app.current_user_role() = 'ADMIN'::text))));
CREATE POLICY user_credentials_select ON public.user_credentials FOR SELECT USING (((app.auth_context() = 'AUTHENTICATED'::text) AND ((user_id = app.current_user_id()) OR (app.current_user_role() = 'ADMIN'::text))));
CREATE POLICY user_credentials_update ON public.user_credentials FOR UPDATE USING (((app.auth_context() = 'AUTHENTICATED'::text) AND ((user_id = app.current_user_id()) OR (app.current_user_role() = 'ADMIN'::text)))) WITH CHECK (((app.auth_context() = 'AUTHENTICATED'::text) AND ((user_id = app.current_user_id()) OR (app.current_user_role() = 'ADMIN'::text))));
CREATE POLICY users_insert ON public.users FOR INSERT WITH CHECK (((app.auth_context() = 'AUTHENTICATED'::text) AND (app.current_user_role() = 'ADMIN'::text)));
CREATE POLICY users_select ON public.users FOR SELECT USING (((app.auth_context() = 'AUTHENTICATED'::text) AND ((id = app.current_user_id()) OR (app.current_user_role() = 'ADMIN'::text))));
CREATE POLICY users_update ON public.users FOR UPDATE USING (((app.auth_context() = 'AUTHENTICATED'::text) AND ((id = app.current_user_id()) OR (app.current_user_role() = 'ADMIN'::text)))) WITH CHECK (((app.auth_context() = 'AUTHENTICATED'::text) AND ((id = app.current_user_id()) OR (app.current_user_role() = 'ADMIN'::text))));

-- -----------------------------------------------------------------------------
-- Datos iniciales
-- -----------------------------------------------------------------------------
-- Roles (ids fijos: el frontend los usa)
insert into public.roles (id, code, name, description) overriding system value values
  (1, 'ADMIN',      'Administrador', 'Gestión de usuarios, roles, parametrización y auditoría'),
  (2, 'SUPERVISOR', 'Supervisor',    'Consulta de estadísticas y gestión de su propio perfil'),
  (3, 'ANALISTA',   'Analista',      'Realiza análisis léxico y consulta su propio historial');
alter table public.roles alter column id restart with 4;

-- Diccionario de entidades del analizador
insert into public.entity_dictionary (value, normalized_value, canonical_value, entity_type, language, source) values
  ('Argentina', 'argentina', 'Argentina', 'COUNTRY', 'ES', 'manual'),
  ('España', 'españa', 'España', 'COUNTRY', 'ES', 'manual'),
  ('Guatemala', 'guatemala', 'Guatemala', 'COUNTRY', 'ES', 'manual'),
  ('México', 'méxico', 'México', 'COUNTRY', 'ES', 'manual'),
  ('Google', 'google', 'Google', 'ORGANIZATION', 'ES', 'manual'),
  ('Microsoft', 'microsoft', 'Microsoft', 'ORGANIZATION', 'ES', 'manual'),
  ('OpenAI', 'openai', 'OpenAI', 'ORGANIZATION', 'ES', 'manual'),
  ('أحمد', 'أحمد', 'أحمد', 'PERSON', 'AR', 'seed'),
  ('أمل', 'أمل', 'أمل', 'PERSON', 'AR', 'seed'),
  ('إبراهيم', 'إبراهيم', 'إبراهيم', 'PERSON', 'AR', 'seed'),
  ('حسن', 'حسن', 'حسن', 'PERSON', 'AR', 'seed'),
  ('حسين', 'حسين', 'حسين', 'PERSON', 'AR', 'seed'),
  ('خالد', 'خالد', 'خالد', 'PERSON', 'AR', 'seed'),
  ('خديجة', 'خديجة', 'خديجة', 'PERSON', 'AR', 'seed'),
  ('رامي', 'رامي', 'رامي', 'PERSON', 'AR', 'seed'),
  ('رشيد', 'رشيد', 'رشيد', 'PERSON', 'AR', 'seed'),
  ('زينب', 'زينب', 'زينب', 'PERSON', 'AR', 'seed'),
  ('سارة', 'سارة', 'سارة', 'PERSON', 'AR', 'seed'),
  ('سامي', 'سامي', 'سامي', 'PERSON', 'AR', 'seed'),
  ('سعيد', 'سعيد', 'سعيد', 'PERSON', 'AR', 'seed'),
  ('طارق', 'طارق', 'طارق', 'PERSON', 'AR', 'seed'),
  ('عائشة', 'عائشة', 'عائشة', 'PERSON', 'AR', 'seed'),
  ('عبدالله', 'عبدالله', 'عبدالله', 'PERSON', 'AR', 'seed'),
  ('علي', 'علي', 'علي', 'PERSON', 'AR', 'seed'),
  ('عمر', 'عمر', 'عمر', 'PERSON', 'AR', 'seed'),
  ('فاطمة', 'فاطمة', 'فاطمة', 'PERSON', 'AR', 'seed'),
  ('كريم', 'كريم', 'كريم', 'PERSON', 'AR', 'seed'),
  ('ليلى', 'ليلى', 'ليلى', 'PERSON', 'AR', 'seed'),
  ('محمد', 'محمد', 'محمد', 'PERSON', 'AR', 'seed'),
  ('مريم', 'مريم', 'مريم', 'PERSON', 'AR', 'seed'),
  ('منى', 'منى', 'منى', 'PERSON', 'AR', 'seed'),
  ('نبيل', 'نبيل', 'نبيل', 'PERSON', 'AR', 'seed'),
  ('نور', 'نور', 'نور', 'PERSON', 'AR', 'seed'),
  ('هدى', 'هدى', 'هدى', 'PERSON', 'AR', 'seed'),
  ('وليد', 'وليد', 'وليد', 'PERSON', 'AR', 'seed'),
  ('ياسر', 'ياسر', 'ياسر', 'PERSON', 'AR', 'seed'),
  ('يوسف', 'يوسف', 'يوسف', 'PERSON', 'AR', 'seed'),
  ('Anthony', 'anthony', 'Anthony', 'PERSON', 'EN', 'seed'),
  ('Barbara', 'barbara', 'Barbara', 'PERSON', 'EN', 'seed'),
  ('Betty', 'betty', 'Betty', 'PERSON', 'EN', 'seed'),
  ('Charles', 'charles', 'Charles', 'PERSON', 'EN', 'seed'),
  ('Christopher', 'christopher', 'Christopher', 'PERSON', 'EN', 'seed'),
  ('Daniel', 'daniel', 'Daniel', 'PERSON', 'EN', 'seed'),
  ('David', 'david', 'David', 'PERSON', 'EN', 'seed'),
  ('Elizabeth', 'elizabeth', 'Elizabeth', 'PERSON', 'EN', 'seed'),
  ('James', 'james', 'James', 'PERSON', 'EN', 'seed'),
  ('Jennifer', 'jennifer', 'Jennifer', 'PERSON', 'EN', 'seed'),
  ('Jessica', 'jessica', 'Jessica', 'PERSON', 'EN', 'seed'),
  ('John', 'john', 'John', 'PERSON', 'EN', 'seed'),
  ('Joseph', 'joseph', 'Joseph', 'PERSON', 'EN', 'seed'),
  ('Karen', 'karen', 'Karen', 'PERSON', 'EN', 'seed'),
  ('Linda', 'linda', 'Linda', 'PERSON', 'EN', 'seed'),
  ('Lisa', 'lisa', 'Lisa', 'PERSON', 'EN', 'seed'),
  ('Margaret', 'margaret', 'Margaret', 'PERSON', 'EN', 'seed'),
  ('Mark', 'mark', 'Mark', 'PERSON', 'EN', 'seed'),
  ('Mary', 'mary', 'Mary', 'PERSON', 'EN', 'seed'),
  ('Matthew', 'matthew', 'Matthew', 'PERSON', 'EN', 'seed'),
  ('Michael', 'michael', 'Michael', 'PERSON', 'EN', 'seed'),
  ('Nancy', 'nancy', 'Nancy', 'PERSON', 'EN', 'seed'),
  ('Patricia', 'patricia', 'Patricia', 'PERSON', 'EN', 'seed'),
  ('Richard', 'richard', 'Richard', 'PERSON', 'EN', 'seed'),
  ('Robert', 'robert', 'Robert', 'PERSON', 'EN', 'seed'),
  ('Sandra', 'sandra', 'Sandra', 'PERSON', 'EN', 'seed'),
  ('Sarah', 'sarah', 'Sarah', 'PERSON', 'EN', 'seed'),
  ('Susan', 'susan', 'Susan', 'PERSON', 'EN', 'seed'),
  ('Thomas', 'thomas', 'Thomas', 'PERSON', 'EN', 'seed'),
  ('William', 'william', 'William', 'PERSON', 'EN', 'seed'),
  ('Alejandro', 'alejandro', 'Alejandro', 'PERSON', 'ES', 'seed'),
  ('Ana', 'ana', 'Ana', 'PERSON', 'ES', 'seed'),
  ('Ana', 'ana', 'Ana', 'PERSON', 'ES', 'manual'),
  ('Andrés', 'andrés', 'Andrés', 'PERSON', 'ES', 'seed'),
  ('Camila', 'camila', 'Camila', 'PERSON', 'ES', 'seed'),
  ('Carlos', 'carlos', 'Carlos', 'PERSON', 'ES', 'seed'),
  ('Carlos', 'carlos', 'Carlos', 'PERSON', 'ES', 'manual'),
  ('Carmen', 'carmen', 'Carmen', 'PERSON', 'ES', 'seed'),
  ('Daniela', 'daniela', 'Daniela', 'PERSON', 'ES', 'seed'),
  ('Diego', 'diego', 'Diego', 'PERSON', 'ES', 'seed'),
  ('Eduardo', 'eduardo', 'Eduardo', 'PERSON', 'ES', 'seed'),
  ('Elena', 'elena', 'Elena', 'PERSON', 'ES', 'seed'),
  ('Fernando', 'fernando', 'Fernando', 'PERSON', 'ES', 'seed'),
  ('Gabriela', 'gabriela', 'Gabriela', 'PERSON', 'ES', 'seed'),
  ('Isabella', 'isabella', 'Isabella', 'PERSON', 'ES', 'seed'),
  ('Javier', 'javier', 'Javier', 'PERSON', 'ES', 'seed'),
  ('Jorge', 'jorge', 'Jorge', 'PERSON', 'ES', 'seed'),
  ('José', 'josé', 'José', 'PERSON', 'ES', 'seed'),
  ('Juan', 'juan', 'Juan', 'PERSON', 'ES', 'seed'),
  ('Juan', 'juan', 'Juan', 'PERSON', 'ES', 'manual'),
  ('Laura', 'laura', 'Laura', 'PERSON', 'ES', 'seed'),
  ('Lucía', 'lucía', 'Lucía', 'PERSON', 'ES', 'seed'),
  ('Luis', 'luis', 'Luis', 'PERSON', 'ES', 'seed'),
  ('Manuel', 'manuel', 'Manuel', 'PERSON', 'ES', 'seed'),
  ('María', 'maría', 'María', 'PERSON', 'ES', 'manual'),
  ('María', 'maría', 'María', 'PERSON', 'ES', 'seed'),
  ('Mariana', 'mariana', 'Mariana', 'PERSON', 'ES', 'seed'),
  ('Miguel', 'miguel', 'Miguel', 'PERSON', 'ES', 'seed'),
  ('Paula', 'paula', 'Paula', 'PERSON', 'ES', 'seed'),
  ('Pedro', 'pedro', 'Pedro', 'PERSON', 'ES', 'manual'),
  ('Pedro', 'pedro', 'Pedro', 'PERSON', 'ES', 'seed'),
  ('Ricardo', 'ricardo', 'Ricardo', 'PERSON', 'ES', 'seed'),
  ('Roberto', 'roberto', 'Roberto', 'PERSON', 'ES', 'seed'),
  ('Sofía', 'sofía', 'Sofía', 'PERSON', 'ES', 'seed'),
  ('Valentina', 'valentina', 'Valentina', 'PERSON', 'ES', 'seed'),
  ('Александр', 'александр', 'Александр', 'PERSON', 'RU', 'seed'),
  ('Алексей', 'алексей', 'Алексей', 'PERSON', 'RU', 'seed'),
  ('Андрей', 'андрей', 'Андрей', 'PERSON', 'RU', 'seed'),
  ('Анна', 'анна', 'Анна', 'PERSON', 'RU', 'seed'),
  ('Артём', 'артём', 'Артём', 'PERSON', 'RU', 'seed'),
  ('Вера', 'вера', 'Вера', 'PERSON', 'RU', 'seed'),
  ('Виктор', 'виктор', 'Виктор', 'PERSON', 'RU', 'seed'),
  ('Владимир', 'владимир', 'Владимир', 'PERSON', 'RU', 'seed'),
  ('Галина', 'галина', 'Галина', 'PERSON', 'RU', 'seed'),
  ('Денис', 'денис', 'Денис', 'PERSON', 'RU', 'seed'),
  ('Дмитрий', 'дмитрий', 'Дмитрий', 'PERSON', 'RU', 'seed'),
  ('Евгений', 'евгений', 'Евгений', 'PERSON', 'RU', 'seed'),
  ('Екатерина', 'екатерина', 'Екатерина', 'PERSON', 'RU', 'seed'),
  ('Елена', 'елена', 'Елена', 'PERSON', 'RU', 'seed'),
  ('Иван', 'иван', 'Иван', 'PERSON', 'RU', 'seed'),
  ('Ирина', 'ирина', 'Ирина', 'PERSON', 'RU', 'seed'),
  ('Людмила', 'людмила', 'Людмила', 'PERSON', 'RU', 'seed'),
  ('Максим', 'максим', 'Максим', 'PERSON', 'RU', 'seed'),
  ('Марина', 'марина', 'Марина', 'PERSON', 'RU', 'seed'),
  ('Мария', 'мария', 'Мария', 'PERSON', 'RU', 'seed'),
  ('Михаил', 'михаил', 'Михаил', 'PERSON', 'RU', 'seed'),
  ('Наталья', 'наталья', 'Наталья', 'PERSON', 'RU', 'seed'),
  ('Николай', 'николай', 'Николай', 'PERSON', 'RU', 'seed'),
  ('Оксана', 'оксана', 'Оксана', 'PERSON', 'RU', 'seed'),
  ('Ольга', 'ольга', 'Ольга', 'PERSON', 'RU', 'seed'),
  ('Павел', 'павел', 'Павел', 'PERSON', 'RU', 'seed'),
  ('Светлана', 'светлана', 'Светлана', 'PERSON', 'RU', 'seed'),
  ('Сергей', 'сергей', 'Сергей', 'PERSON', 'RU', 'seed'),
  ('Татьяна', 'татьяна', 'Татьяна', 'PERSON', 'RU', 'seed'),
  ('Юлия', 'юлия', 'Юлия', 'PERSON', 'RU', 'seed'),
  ('于洋', '于洋', '于洋', 'PERSON', 'ZH', 'seed'),
  ('何军', '何军', '何军', 'PERSON', 'ZH', 'seed'),
  ('冯刚', '冯刚', '冯刚', 'PERSON', 'ZH', 'seed'),
  ('刘洋', '刘洋', '刘洋', 'PERSON', 'ZH', 'seed'),
  ('吴超', '吴超', '吴超', 'PERSON', 'ZH', 'seed'),
  ('周杰', '周杰', '周杰', 'PERSON', 'ZH', 'seed'),
  ('唐波', '唐波', '唐波', 'PERSON', 'ZH', 'seed'),
  ('孙丽', '孙丽', '孙丽', 'PERSON', 'ZH', 'seed'),
  ('宋阳', '宋阳', '宋阳', 'PERSON', 'ZH', 'seed'),
  ('张伟', '张伟', '张伟', 'PERSON', 'ZH', 'seed'),
  ('徐涛', '徐涛', '徐涛', 'PERSON', 'ZH', 'seed'),
  ('朱峰', '朱峰', '朱峰', 'PERSON', 'ZH', 'seed'),
  ('李娜', '李娜', '李娜', 'PERSON', 'ZH', 'seed'),
  ('杨帆', '杨帆', '杨帆', 'PERSON', 'ZH', 'seed'),
  ('林静', '林静', '林静', 'PERSON', 'ZH', 'seed'),
  ('梁华', '梁华', '梁华', 'PERSON', 'ZH', 'seed'),
  ('王伟', '王伟', '王伟', 'PERSON', 'ZH', 'seed'),
  ('罗兰', '罗兰', '罗兰', 'PERSON', 'ZH', 'seed'),
  ('胡明', '胡明', '胡明', 'PERSON', 'ZH', 'seed'),
  ('萧然', '萧然', '萧然', 'PERSON', 'ZH', 'seed'),
  ('董雪', '董雪', '董雪', 'PERSON', 'ZH', 'seed'),
  ('谢欣', '谢欣', '谢欣', 'PERSON', 'ZH', 'seed'),
  ('赵磊', '赵磊', '赵磊', 'PERSON', 'ZH', 'seed'),
  ('郑东', '郑东', '郑东', 'PERSON', 'ZH', 'seed'),
  ('郭强', '郭强', '郭强', 'PERSON', 'ZH', 'seed'),
  ('陈静', '陈静', '陈静', 'PERSON', 'ZH', 'seed'),
  ('韩梅', '韩梅', '韩梅', 'PERSON', 'ZH', 'seed'),
  ('马超', '马超', '马超', 'PERSON', 'ZH', 'seed'),
  ('高云', '高云', '高云', 'PERSON', 'ZH', 'seed'),
  ('黄敏', '黄敏', '黄敏', 'PERSON', 'ZH', 'seed'),
  ('Python', 'python', 'Python', 'PRODUCT', 'ES', 'manual'),
  ('Windows', 'windows', 'Windows', 'PRODUCT', 'ES', 'manual');

-- -----------------------------------------------------------------------------
-- Storage (solo en Supabase): buckets privados que usa el backend
-- -----------------------------------------------------------------------------
do $$
begin
  if to_regclass('storage.buckets') is not null then
    insert into storage.buckets (id, name, public) values
      ('profile-photos',   'profile-photos',   false),  -- fotos original y editada
      ('credentials',      'credentials',      false),  -- PDF de credenciales
      ('stickers',         'stickers',         false),  -- global/ (sistema) y users/{id}/ (propios)
      ('analysis-files',   'analysis-files',   false),  -- archivos .txt cargados
      ('analysis-reports', 'analysis-reports', false)   -- reportes PDF
    on conflict (id) do nothing;
  end if;
end $$;
